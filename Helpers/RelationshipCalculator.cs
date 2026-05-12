using MyStoryTold.Models;

namespace MyStoryTold.Helpers;

/// <summary>
/// Derives the proper kinship term (Grandfather, Niece, Cousin, Aunt,
/// Father-in-law, etc.) from a family-tree graph by walking parent /
/// child / spouse edges between "self" and each target node.
///
/// Doesn't try to be a full genealogy engine — covers the common cases
/// up to second-cousin / great-grandparent and falls back to "Relative"
/// when nothing tighter fits.
/// </summary>
public class RelationshipCalculator
{
    private readonly Dictionary<int, List<int>> _parents;   // child  → parent ids
    private readonly Dictionary<int, List<int>> _children;  // parent → child ids
    private readonly Dictionary<int, HashSet<int>> _spouses; // node → spouse ids
    private readonly Dictionary<int, Gender> _genderOf;
    private readonly int _selfId;

    private enum Gender { Unknown, Male, Female }

    public RelationshipCalculator(
        int selfId,
        IEnumerable<FamilyTreeNode> nodes,
        IEnumerable<FamilyRelationship> edges,
        Func<FamilyTreeNode, string?>? customGenderHint = null)
    {
        _selfId = selfId;
        _parents = nodes.ToDictionary(n => n.Id, _ => new List<int>());
        _children = nodes.ToDictionary(n => n.Id, _ => new List<int>());
        _spouses = nodes.ToDictionary(n => n.Id, _ => new HashSet<int>());
        _genderOf = nodes.ToDictionary(n => n.Id, n => InferGender(n, customGenderHint));

        foreach (var e in edges)
        {
            switch (e.RelType)
            {
                case FamilyRelationType.Parent:
                    if (_parents.ContainsKey(e.ToNodeId)) _parents[e.ToNodeId].Add(e.FromNodeId);
                    if (_children.ContainsKey(e.FromNodeId)) _children[e.FromNodeId].Add(e.ToNodeId);
                    break;
                case FamilyRelationType.Spouse:
                    if (_spouses.ContainsKey(e.FromNodeId)) _spouses[e.FromNodeId].Add(e.ToNodeId);
                    if (_spouses.ContainsKey(e.ToNodeId)) _spouses[e.ToNodeId].Add(e.FromNodeId);
                    break;
                case FamilyRelationType.Sibling:
                    // Treated downstream as "shared parent" rather than its
                    // own edge type — explicit sibling edges only fire when
                    // we can't reach the target through parents.
                    break;
            }
        }
    }

    /// <summary>
    /// Returns the kinship term from self to the target node ("Grandfather",
    /// "Niece", "Husband", "Cousin", "Relative", or null if target is self).
    /// </summary>
    public string? Compute(int targetId)
    {
        if (targetId == _selfId) return null;

        // Direct spouse.
        if (_spouses.GetValueOrDefault(_selfId)?.Contains(targetId) == true)
            return GenderedTerm(targetId, "Husband", "Wife", "Spouse");

        var ancestorsOfSelf  = WalkUp(_selfId);          // ancestorId → generation distance (1 = parent)
        var descendantsOfSelf = WalkDown(_selfId);       // descendantId → generation distance (1 = child)

        // Direct ancestor (parent / grandparent / great-grandparent / ...)
        if (ancestorsOfSelf.TryGetValue(targetId, out var upGen))
        {
            return AncestorTerm(targetId, upGen);
        }

        // Direct descendant (child / grandchild / great-grandchild / ...)
        if (descendantsOfSelf.TryGetValue(targetId, out var downGen))
        {
            return DescendantTerm(targetId, downGen);
        }

        // Lateral via shared ancestor — sibling, niece/nephew, aunt/uncle,
        // cousin, etc. We try every ancestor of self; for each, see how
        // many steps down from that ancestor lead to the target.
        var ancestorsOfTarget = WalkUp(targetId);
        var bestUp = int.MaxValue;
        var bestDown = int.MaxValue;
        foreach (var (anc, up) in ancestorsOfSelf.Append(new KeyValuePair<int, int>(_selfId, 0)))
        {
            if (!ancestorsOfTarget.TryGetValue(anc, out var down)) continue;
            // Closer ancestors win (lower total distance).
            if (up + down < bestUp + bestDown)
            {
                bestUp = up;
                bestDown = down;
            }
        }
        if (bestUp == int.MaxValue)
        {
            // Last resort: target via spouse of someone related.
            foreach (var sp in _spouses.GetValueOrDefault(targetId) ?? new())
            {
                if (sp == _selfId) continue;
                var r = ComputeForRelated(sp);
                if (r != null) return InLaw(r);
            }
            return "Relative";
        }
        return LateralTerm(targetId, bestUp, bestDown);
    }

    // ───── Helpers ─────────────────────────────────────────────────────

    private string AncestorTerm(int nodeId, int gen)
    {
        // gen 1 → Father/Mother. gen 2 → Grand{father}. gen 3 →
        // Great-grand{father}. gen 4+ → "1st great-grand{father}",
        // "2nd great-grand…" (the user's preferred ordinal form for
        // deep lineage labels — easier to read than "Great-Great-Great-").
        if (gen == 1) return GenderedTerm(nodeId, "Father", "Mother", "Parent");
        var prefix = LinealPrefix(gen);
        return GenderedTerm(nodeId, prefix + "father", prefix + "mother", prefix + "parent");
    }

    private string DescendantTerm(int nodeId, int gen)
    {
        if (gen == 1) return GenderedTerm(nodeId, "Son", "Daughter", "Child");
        var prefix = LinealPrefix(gen);
        return GenderedTerm(nodeId, prefix + "son", prefix + "daughter", prefix + "child");
    }

    /// <summary>"Grand", "Great-grand", "1st great-grand", … — the prefix
    /// applied to lineal-relation terms beyond Father/Son.</summary>
    private static string LinealPrefix(int gen)
    {
        if (gen <= 1) return "";
        if (gen == 2) return "Grand";
        if (gen == 3) return "Great-grand";
        return $"{ShortOrdinal(gen - 3)} great-grand";
    }

    private string LateralTerm(int nodeId, int up, int down)
    {
        // up = steps from self up to common ancestor
        // down = steps from common ancestor down to target
        // (up=0, down=N) → descendant (handled earlier)
        // (up=N, down=0) → ancestor (handled earlier)
        // (up=1, down=1) → sibling
        // (up=1, down=2) → niece/nephew
        // (up=1, down=3+) → grand/great-grand niece/nephew
        // (up=2, down=1) → aunt/uncle
        // (up=2, down=2) → cousin
        // (up=2, down=3) → cousin's child (first cousin once removed)
        // (up=3+, down=1) → great-aunt/uncle
        if (up == 1 && down == 1)
            return GenderedTerm(nodeId, "Brother", "Sister", "Sibling");
        if (up == 1 && down == 2)
            return GenderedTerm(nodeId, "Nephew", "Niece", "Niece/Nephew");
        if (up == 1 && down >= 3)
        {
            // Grandnephew (down=3) → Great-grandnephew (down=4) →
            // 1st great-grandnephew (down=5), 2nd great… same ordinal
            // convention as lineal great-grand.
            var pre = LinealPrefix(down - 1);
            return GenderedTerm(nodeId, pre + "nephew", pre + "niece", pre + "niece/nephew");
        }
        if (up == 2 && down == 1)
            return GenderedTerm(nodeId, "Uncle", "Aunt", "Aunt/Uncle");
        if (up >= 3 && down == 1)
        {
            // Great-uncle (up=3) → 1st great-uncle (up=4) → 2nd … —
            // consistent with the great-grandparent ordinal convention.
            var pre = up == 3 ? "Great-" : $"{ShortOrdinal(up - 3)} great-";
            return GenderedTerm(nodeId, pre + "uncle", pre + "aunt", pre + "aunt/uncle");
        }
        if (up == 2 && down == 2) return "Cousin";
        if (up >= 3 && down >= 3 && up == down) return $"{NthOrdinal(Math.Min(up, down) - 1)} cousin";
        if (Math.Abs(up - down) >= 1 && Math.Min(up, down) >= 2)
            return $"{NthOrdinal(Math.Min(up, down) - 1)} cousin {Math.Abs(up - down)}x removed";
        return "Relative";
    }

    private string InLaw(string related) => related switch
    {
        "Father" => "Father-in-law",
        "Mother" => "Mother-in-law",
        "Parent" => "Parent-in-law",
        "Son"    => "Son-in-law",
        "Daughter" => "Daughter-in-law",
        "Child"  => "Child-in-law",
        "Brother" => "Brother-in-law",
        "Sister"  => "Sister-in-law",
        "Sibling" => "Sibling-in-law",
        _ => related + " (by marriage)"
    };

    private string? ComputeForRelated(int otherId)
    {
        // Cheap re-entry guard against recursion: only follow spouses one
        // hop. If `other` itself is reachable through blood, compute that.
        var ancestorsOfSelf = WalkUp(_selfId);
        var descendantsOfSelf = WalkDown(_selfId);
        if (ancestorsOfSelf.TryGetValue(otherId, out var u)) return AncestorTerm(otherId, u);
        if (descendantsOfSelf.TryGetValue(otherId, out var d)) return DescendantTerm(otherId, d);
        var ancOfOther = WalkUp(otherId);
        foreach (var (anc, up) in ancestorsOfSelf)
        {
            if (ancOfOther.TryGetValue(anc, out var down))
            {
                return LateralTerm(otherId, up, down);
            }
        }
        return null;
    }

    private Dictionary<int, int> WalkUp(int start)
    {
        var result = new Dictionary<int, int>();
        var queue = new Queue<(int Id, int Dist)>();
        queue.Enqueue((start, 0));
        while (queue.Count > 0)
        {
            var (id, dist) = queue.Dequeue();
            if (!_parents.TryGetValue(id, out var ps)) continue;
            foreach (var p in ps)
            {
                if (result.ContainsKey(p)) continue;
                result[p] = dist + 1;
                queue.Enqueue((p, dist + 1));
            }
        }
        return result;
    }

    private Dictionary<int, int> WalkDown(int start)
    {
        var result = new Dictionary<int, int>();
        var queue = new Queue<(int Id, int Dist)>();
        queue.Enqueue((start, 0));
        while (queue.Count > 0)
        {
            var (id, dist) = queue.Dequeue();
            if (!_children.TryGetValue(id, out var cs)) continue;
            foreach (var c in cs)
            {
                if (result.ContainsKey(c)) continue;
                result[c] = dist + 1;
                queue.Enqueue((c, dist + 1));
            }
        }
        return result;
    }

    private string GenderedTerm(int nodeId, string male, string female, string neutral)
    {
        return _genderOf.GetValueOrDefault(nodeId, Gender.Unknown) switch
        {
            Gender.Male => male,
            Gender.Female => female,
            _ => neutral
        };
    }

    private static Gender InferGender(FamilyTreeNode n, Func<FamilyTreeNode, string?>? customHint)
    {
        // Member: use ApplicationUser.Gender directly.
        if (n.NodeKind == FamilyNodeKind.Member && n.TargetUser != null)
        {
            var g = (n.TargetUser.Gender ?? "").Trim().ToLowerInvariant();
            if (g.StartsWith("m")) return Gender.Male;
            if (g.StartsWith("f") || g.StartsWith("w")) return Gender.Female;
        }
        // PersonProfile: prefer the explicit Gender field; fall back to
        // the Relation hint (set by the popup's Father/Mother button or
        // typed by the writer) when Gender is unset.
        if (n.NodeKind == FamilyNodeKind.Profile && n.TargetProfile != null)
        {
            var g = (n.TargetProfile.Gender ?? "").Trim().ToLowerInvariant();
            if (g.StartsWith("m")) return Gender.Male;
            if (g.StartsWith("f") || g.StartsWith("w")) return Gender.Female;
        }
        var hint = customHint?.Invoke(n) ?? n.TargetProfile?.Relation ?? "";
        return InferGenderFromHint(hint);
    }

    private static Gender InferGenderFromHint(string hint)
    {
        var h = (hint ?? "").ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(h)) return Gender.Unknown;
        string[] male = {
            "father","dad","papa","papà","papi","grand","granddad","grandfather","grandpa","nonno","nonno paterno","nonno materno",
            "uncle","zio","brother","fratello","son","figlio","husband","marito","sposo","nephew","godfather","padrino","stepfather","step-father",
            "patrigno","cousin (m)"
        };
        string[] female = {
            "mother","mom","mum","mama","mamma","grandmother","granny","grandma","nonna","nonna paterna","nonna materna",
            "aunt","zia","sister","sorella","daughter","figlia","wife","moglie","sposa","niece","godmother","madrina","stepmother","step-mother",
            "matrigna"
        };
        // Order matters — check more specific terms first.
        foreach (var m in male)   if (h.Contains(m)) return Gender.Male;
        foreach (var f in female) if (h.Contains(f)) return Gender.Female;
        return Gender.Unknown;
    }

    private static string NthOrdinal(int n) => n switch
    {
        1 => "First",
        2 => "Second",
        3 => "Third",
        4 => "Fourth",
        5 => "Fifth",
        _ => n + "th"
    };

    /// <summary>Short ordinal form — "1st", "2nd", "3rd", "4th", …</summary>
    private static string ShortOrdinal(int n)
    {
        var lastTwo = n % 100;
        if (lastTwo >= 11 && lastTwo <= 13) return n + "th";
        return (n % 10) switch
        {
            1 => n + "st",
            2 => n + "nd",
            3 => n + "rd",
            _ => n + "th"
        };
    }
}

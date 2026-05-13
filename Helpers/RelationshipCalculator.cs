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

        var siblingEdges = new List<(int a, int b)>();
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
                    // Saved for a second pass — Sibling edges propagate
                    // virtual Parent edges in both directions once all
                    // explicit Parent edges are loaded. Without this,
                    // Livia (added as Mario's sibling before Mario got
                    // parents on the tree) walked up to nothing and
                    // came out as "Relative" instead of "Aunt".
                    siblingEdges.Add((e.FromNodeId, e.ToNodeId));
                    break;
            }
        }
        // Second pass: copy each side's parent list into the other,
        // and update _children accordingly. Two iterations propagate
        // siblings-of-siblings without needing a full fixed-point.
        for (int pass = 0; pass < 2; pass++)
        {
            foreach (var (a, b) in siblingEdges)
            {
                if (!_parents.ContainsKey(a) || !_parents.ContainsKey(b)) continue;
                foreach (var p in _parents[b].ToList())
                {
                    if (!_parents[a].Contains(p))
                    {
                        _parents[a].Add(p);
                        if (_children.ContainsKey(p)) _children[p].Add(a);
                    }
                }
                foreach (var p in _parents[a].ToList())
                {
                    if (!_parents[b].Contains(p))
                    {
                        _parents[b].Add(p);
                        if (_children.ContainsKey(p)) _children[p].Add(b);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Returns the kinship term from self to the target node.
    /// Follows the English-language genealogical canon (matrix of
    /// "generations up to common ancestor × generations down to them")
    /// — see Family_Relationships_Reference.pdf for the authoritative
    /// table. Falls back to "Relative" only when no path at all
    /// connects the two through blood, marriage, or step relations.
    /// </summary>
    public string? Compute(int targetId)
    {
        if (targetId == _selfId) return null;

        // Direct spouse.
        if (_spouses.GetValueOrDefault(_selfId)?.Contains(targetId) == true)
            return GenderedTerm(targetId, "Husband", "Wife", "Spouse");

        var ancOfSelf   = WalkUp(_selfId);
        var ancOfTarget = WalkUp(targetId);

        // 1. Blood path via a shared ancestor (includes self/target on
        // their own respective sides at distance 0, so direct ancestor/
        // descendant relationships fall out of the same matrix lookup).
        var (bestUp, bestDown) = FindClosestCommon(ancOfSelf, ancOfTarget, _selfId, targetId);
        if (bestUp != int.MaxValue)
        {
            return MatrixTerm(targetId, bestUp, bestDown);
        }

        // 2. In-law via SELF's spouse (target is a blood relative of my
        // spouse). The matrix lookup runs from spouse's side instead of
        // mine; the result wraps in `InLawOf`. Aunt/Uncle/Cousin in-laws
        // get the simplified labels per the reference (PDF: "almost
        // always just called aunt/uncle/cousin in practice").
        foreach (var selfSpouse in _spouses.GetValueOrDefault(_selfId) ?? new())
        {
            if (selfSpouse == targetId) continue;
            var ancOfSpouse = WalkUp(selfSpouse);
            var (spUp, spDown) = FindClosestCommon(ancOfSpouse, ancOfTarget, selfSpouse, targetId);
            if (spUp != int.MaxValue)
            {
                return InLawOf(MatrixTerm(targetId, spUp, spDown));
            }
        }

        // 3. In-law via TARGET's spouse (target is married to a blood
        // relative of mine — Uncle Ivo = married to Aunt Livia who's my
        // father's sister). Render with target's gender; if unknown,
        // fall back to the opposite of the spouse's gender (Ivo's
        // gender unset + Livia female → Ivo defaults male → "Uncle").
        foreach (var sp in _spouses.GetValueOrDefault(targetId) ?? new())
        {
            if (sp == _selfId) continue;
            var ancOfSp = WalkUp(sp);
            var (sUp, sDown) = FindClosestCommon(ancOfSelf, ancOfSp, _selfId, sp);
            if (sUp != int.MaxValue)
            {
                var origG = _genderOf.GetValueOrDefault(targetId, Gender.Unknown);
                if (origG == Gender.Unknown)
                {
                    var spG = _genderOf.GetValueOrDefault(sp, Gender.Unknown);
                    var flip = spG == Gender.Male ? Gender.Female
                             : spG == Gender.Female ? Gender.Male
                             : Gender.Unknown;
                    if (flip != Gender.Unknown) _genderOf[targetId] = flip;
                }
                try { return InLawOf(MatrixTerm(targetId, sUp, sDown)); }
                finally { _genderOf[targetId] = origG; }
            }
        }
        return "Relative";
    }

    /// <summary>Find the shortest combined-distance common ancestor
    /// between two node-id sets. Each side's map already excludes self,
    /// so we splice in (sideId, 0) to model "self/target IS the common
    /// ancestor" (descendants/ancestors of one another).</summary>
    private static (int up, int down) FindClosestCommon(
        Dictionary<int,int> ancOfA, Dictionary<int,int> ancOfB,
        int sideA, int sideB)
    {
        int bestU = int.MaxValue, bestD = int.MaxValue;
        // A as the common ancestor (A IS an ancestor of B)
        if (ancOfB.TryGetValue(sideA, out var aDown))
        {
            if (aDown < bestU + bestD) { bestU = 0; bestD = aDown; }
        }
        // B as the common ancestor (B IS an ancestor of A)
        if (ancOfA.TryGetValue(sideB, out var aUp))
        {
            if (aUp < bestU + bestD) { bestU = aUp; bestD = 0; }
        }
        // Generic case — they meet at some common third ancestor.
        foreach (var (anc, up) in ancOfA)
        {
            if (!ancOfB.TryGetValue(anc, out var down)) continue;
            if (up + down < bestU + bestD) { bestU = up; bestD = down; }
        }
        return (bestU, bestD);
    }

    // ───── Helpers ─────────────────────────────────────────────────────

    /// <summary>The kinship-matrix cell at (up, down). up = generations
    /// from SELF up to the shared ancestor; down = generations from the
    /// shared ancestor down to TARGET. Direct ancestor = (N, 0); direct
    /// descendant = (0, N); sibling = (1, 1); aunt/uncle = (2, 1);
    /// 1st cousin = (2, 2); 1st cousin once removed = (2, 3) or (3, 2);
    /// etc. Matches the table in Family_Relationships_Reference.pdf.</summary>
    private string MatrixTerm(int targetId, int up, int down)
    {
        // Direct descendant: ancestor is SELF, target is below me.
        if (up == 0 && down >= 1)
        {
            if (down == 1) return GenderedTerm(targetId, "Son", "Daughter", "Child");
            var p = LinealPrefix(down);
            return GenderedTerm(targetId, p + "son", p + "daughter", p + "child");
        }
        // Direct ancestor: target is my ancestor.
        if (down == 0 && up >= 1)
        {
            if (up == 1) return GenderedTerm(targetId, "Father", "Mother", "Parent");
            var p = LinealPrefix(up);
            return GenderedTerm(targetId, p + "father", p + "mother", p + "parent");
        }
        // Sibling: same parents.
        if (up == 1 && down == 1)
            return GenderedTerm(targetId, "Brother", "Sister", "Sibling");
        // Aunt / Uncle and their great- variants. down == 1, up >= 2.
        if (down == 1 && up >= 2)
        {
            var p = AuntUnclePrefix(up);
            return GenderedTerm(targetId, p + "uncle", p + "aunt", p + "aunt/uncle");
        }
        // Niece / Nephew and their grand-/great-grand- variants. up == 1, down >= 2.
        if (up == 1 && down >= 2)
        {
            var p = NieceNephewPrefix(down);
            return GenderedTerm(targetId, p + "nephew", p + "niece", p + "niece/nephew");
        }
        // Cousins. Quick rule from the reference:
        //   degree  = min(up, down) - 1
        //   removed = |up - down|
        // E.g. up=2,down=2 → 1st cousin. up=3,down=2 → 1st cousin once
        // removed. up=4,down=3 → 2nd cousin once removed. up=3,down=3 →
        // 2nd cousin.
        int degree  = Math.Min(up, down) - 1;
        int removed = Math.Abs(up - down);
        var deg = ShortOrdinal(degree);
        return removed switch
        {
            0 => $"{deg} cousin",
            1 => $"{deg} cousin once removed",
            2 => $"{deg} cousin twice removed",
            _ => $"{deg} cousin {removed}× removed"
        };
    }

    /// <summary>"Grand", "Great-grand", "2nd great-grand", … — the prefix
    /// applied to lineal-relation terms beyond Father/Son. gen 2 = Grand;
    /// gen 3 = Great-grand; gen 4 = 2nd great-grand (PDF convention).</summary>
    private static string LinealPrefix(int gen)
    {
        if (gen <= 1) return "";
        if (gen == 2) return "Grand";
        if (gen == 3) return "Great-grand";
        // PDF: 2nd great-grandparent at gen=4, 3rd at gen=5, …
        return $"{ShortOrdinal(gen - 2)} great-grand";
    }

    /// <summary>"" (Aunt/Uncle), "Great-" (Great-aunt at up=3), "2nd great-",
    /// "3rd great-" … per the PDF.</summary>
    private static string AuntUnclePrefix(int up)
    {
        if (up == 2) return "";
        if (up == 3) return "Great-";
        return $"{ShortOrdinal(up - 2)} great-";
    }

    /// <summary>"" (Niece/Nephew), "Grand-" (grand-niece at down=3),
    /// "Great-grand-", "2nd great-grand-" … per the PDF.</summary>
    private static string NieceNephewPrefix(int down)
    {
        if (down == 2) return "";
        if (down == 3) return "Grand-";
        if (down == 4) return "Great-grand-";
        return $"{ShortOrdinal(down - 3)} great-grand-";
    }

    /// <summary>Convert a blood-relation term into its in-law form per
    /// the PDF reference. Aunts, uncles, and cousins by marriage are
    /// collapsed back to the plain term (PDF: "almost always just
    /// called aunt/uncle/cousin in practice"). Distant cousin-in-laws
    /// get the "-in-law" suffix rather than "(by marriage)" since the
    /// user explicitly rejected the parenthetical for cousin-class
    /// relationships.</summary>
    private string InLawOf(string related)
    {
        // Close, common in-laws — explicit forms.
        switch (related)
        {
            case "Father":   return "Father-in-law";
            case "Mother":   return "Mother-in-law";
            case "Parent":   return "Parent-in-law";
            case "Son":      return "Son-in-law";
            case "Daughter": return "Daughter-in-law";
            case "Child":    return "Child-in-law";
            case "Brother":  return "Brother-in-law";
            case "Sister":   return "Sister-in-law";
            case "Sibling":  return "Sibling-in-law";
            case "Husband":
            case "Wife":
            case "Spouse":
                // Spouse's spouse is just oneself, no in-law label needed.
                return related;
        }
        // Aunt/Uncle by marriage → just "Aunt"/"Uncle" (PDF).
        if (related == "Aunt" || related == "Uncle" || related == "Aunt/Uncle"
            || related.EndsWith(" aunt") || related.EndsWith(" uncle") || related.EndsWith(" aunt/uncle"))
            return related;
        // Niece/Nephew by marriage — uncommon but PDF lists it.
        if (related.EndsWith("Niece") || related.EndsWith("niece"))   return related + "-in-law";
        if (related.EndsWith("Nephew") || related.EndsWith("nephew")) return related + "-in-law";
        // Cousin in-law: spouse of cousin, or your spouse's cousin.
        if (related.Contains("cousin")) return related + "-in-law";
        // Lineal in-law (great-grandparent / great-grandchild by marriage)
        // — parenthetical form reads more cleanly than the awkward
        // "great-great-grandfather-in-law".
        return related + " (by marriage)";
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
        // Only trust the explicit Gender field. The Relation field is
        // free-text the writer typed — "Aunt's husband" / "Sister-in-
        // law" / "Aunt" — and a hint-scan flips its sign incorrectly
        // (Ivo "Aunt's husband" got parsed as female because "aunt"
        // matched before "husband"). Cleaner to return Unknown and let
        // the lateral-in-law fallback flip on the spouse's gender.
        if (n.NodeKind == FamilyNodeKind.Member && n.TargetUser != null)
        {
            var g = (n.TargetUser.Gender ?? "").Trim().ToLowerInvariant();
            if (g.StartsWith("m")) return Gender.Male;
            if (g.StartsWith("f") || g.StartsWith("w")) return Gender.Female;
        }
        if (n.NodeKind == FamilyNodeKind.Profile && n.TargetProfile != null)
        {
            var g = (n.TargetProfile.Gender ?? "").Trim().ToLowerInvariant();
            if (g.StartsWith("m")) return Gender.Male;
            if (g.StartsWith("f") || g.StartsWith("w")) return Gender.Female;
        }
        return Gender.Unknown;
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

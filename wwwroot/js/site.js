// My Story Told - Site JavaScript

// ── Home feed pagination ─────────────────────────────────────────────
// Initial render shows ~30 cards; the "Load more" button + a near-
// bottom IntersectionObserver fetch /Home/FeedPage with the current
// cursor (the timestamp of the oldest visible post) and append the
// next batch's HTML directly. Server returns rendered _FeedCard
// markup so we don't duplicate the article-layout logic in JS.
document.addEventListener('DOMContentLoaded', function () {
    var feedList = document.getElementById('feedList');
    var loadWrap = document.getElementById('feedLoadMoreWrap');
    if (!feedList || !loadWrap) return;
    var btn      = document.getElementById('feedLoadMore');
    var endNote  = document.getElementById('feedEnd');
    var loading  = false;

    function setEnd() {
        loadWrap.dataset.end = 'true';
        if (btn) btn.style.display = 'none';
        if (endNote) endNote.style.display = '';
    }
    function loadMore() {
        if (loading) return;
        if (loadWrap.dataset.end === 'true') return;
        var before = loadWrap.dataset.before;
        var take = parseInt(loadWrap.dataset.take || '20', 10);
        if (!before) return;
        loading = true;
        if (btn) { btn.disabled = true; btn.textContent = 'Loading…'; }

        fetch('/Home/FeedPage?before=' + encodeURIComponent(before) + '&take=' + take, {
            credentials: 'same-origin',
            headers: { 'Accept': 'text/html' }
        })
        .then(function (r) { return r.ok ? r.text() : null; })
        .then(function (html) {
            if (html == null) return;
            // Parse the wrapper to read next-cursor + end flag, then
            // graft the cards into the live feed list.
            var tmp = document.createElement('div');
            tmp.innerHTML = html;
            var payload = tmp.querySelector('.feed-page-payload');
            if (!payload) return;
            var next = payload.dataset.next || '';
            var end  = payload.dataset.end === 'true';
            // Move children one by one to preserve event-listener-friendly DOM.
            while (payload.firstChild) {
                feedList.appendChild(payload.firstChild);
            }
            // Re-init read-more handlers on the just-appended cards.
            if (window.kronWireFeedExpanders) window.kronWireFeedExpanders();
            if (next) {
                loadWrap.dataset.before = next;
            } else {
                setEnd();
            }
            if (end) setEnd();
        })
        .catch(function () { /* network blip — leave the button so user can retry */ })
        .finally(function () {
            loading = false;
            if (btn) { btn.disabled = false; btn.textContent = 'Load more'; }
        });
    }

    if (btn) btn.addEventListener('click', loadMore);
    // Auto-trigger when the load-more region is near the viewport.
    if ('IntersectionObserver' in window) {
        var io = new IntersectionObserver(function (entries) {
            if (entries.some(function (e) { return e.isIntersecting; })) loadMore();
        }, { rootMargin: '600px 0px' });
        io.observe(loadWrap);
    }
});

// ── Slow-down popup ─────────────────────────────────────────────────
// Surfaces friendly UI when the server returns 429 (rate limited) on
// any fetch. Inline-mounted, auto-dismisses, never opens twice in a
// row. Other modules call kronShowSlowDown(retryAfterSeconds, message?)
// or wrap a fetch in kronGuardedFetch(input, init) which auto-detects.
(function () {
    var el;
    function ensureEl() {
        if (el) return el;
        el = document.createElement('div');
        el.className = 'kron-slowdown-toast';
        el.setAttribute('role', 'status');
        el.setAttribute('aria-live', 'polite');
        el.style.cssText =
            'position:fixed;bottom:24px;left:50%;transform:translate(-50%,12px);' +
            'background:#2a2520;color:#fff;padding:12px 18px;border-radius:999px;' +
            'box-shadow:0 8px 28px rgba(0,0,0,0.25);font-size:0.92rem;font-weight:600;' +
            'opacity:0;pointer-events:none;transition:opacity .2s,transform .2s;z-index:2000;';
        document.body.appendChild(el);
        return el;
    }
    var lastShownAt = 0;
    window.kronShowSlowDown = function (retryAfter, message) {
        // Throttle: avoid flicker if multiple AJAX calls fail at once.
        var now = Date.now();
        if (now - lastShownAt < 800) return;
        lastShownAt = now;
        var node = ensureEl();
        var secs = (retryAfter && retryAfter > 0) ? Math.round(retryAfter) : 30;
        node.textContent = message || ('🐢 You\'re moving fast — give it ' + secs + 's and try again.');
        node.style.opacity = '1';
        node.style.transform = 'translate(-50%, 0)';
        node.style.pointerEvents = 'auto';
        setTimeout(function () {
            node.style.opacity = '0';
            node.style.transform = 'translate(-50%, 12px)';
            node.style.pointerEvents = 'none';
        }, Math.min(6000, Math.max(2500, secs * 200)));
    };
    // fetch wrapper that auto-shows the popup on 429.
    window.kronGuardedFetch = function (input, init) {
        return fetch(input, init).then(function (res) {
            if (res.status === 429) {
                var ra = parseInt(res.headers.get('Retry-After') || '0', 10);
                window.kronShowSlowDown(ra);
            }
            return res;
        });
    };
})();


// ── Badge hover zoom positioning ─────────────────────────────────────
// The .badge-hover-zoom popover is position:fixed so it escapes the
// dashboard's nested stacking contexts (otherwise the dash-card sitting
// above traps the popover behind itself). We compute its top/left from
// the badge's bounding rect on hover/focus and reposition on scroll +
// resize so the popover stays anchored.
document.addEventListener('DOMContentLoaded', function () {
    var wraps = document.querySelectorAll('.badge-hover-wrap');
    if (!wraps.length) return;

    function positionZoom(wrap) {
        var zoom = wrap.querySelector('.badge-hover-zoom');
        if (!zoom) return;
        var r = wrap.getBoundingClientRect();
        // Width is fixed (280px) but offsetHeight depends on content.
        // We measure both each time so a different language / layout
        // can't make us mis-place.
        var w = zoom.offsetWidth || 280;
        var h = zoom.offsetHeight || 200;
        var left = r.left + r.width / 2 - w / 2;
        var top  = r.top - h - 8;
        // Clamp horizontally to the viewport with an 8px gutter.
        var minLeft = 8;
        var maxLeft = window.innerWidth - w - 8;
        if (left < minLeft) left = minLeft;
        if (left > maxLeft) left = Math.max(minLeft, maxLeft);
        // Flip below the badge if there's no room above.
        if (top < 8) {
            top = r.bottom + 8;
            zoom.style.transformOrigin = 'top center';
        } else {
            zoom.style.transformOrigin = 'bottom center';
        }
        zoom.style.left = left + 'px';
        zoom.style.top  = top + 'px';
    }

    var activeWrap = null;
    wraps.forEach(function (wrap) {
        function show() { activeWrap = wrap; positionZoom(wrap); }
        function hide() { if (activeWrap === wrap) activeWrap = null; }
        wrap.addEventListener('mouseenter', show);
        wrap.addEventListener('mouseleave', hide);
        wrap.addEventListener('focusin',    show);
        wrap.addEventListener('focusout',   hide);
        // Pre-position once so the very first hover doesn't flash at 0,0.
        positionZoom(wrap);
    });

    function reflow() { if (activeWrap) positionZoom(activeWrap); }
    window.addEventListener('scroll', reflow, { passive: true });
    window.addEventListener('resize', reflow, { passive: true });
});

// Global navbar search — small popover, results inline in the main column
document.addEventListener('DOMContentLoaded', function () {
    var toggle  = document.getElementById('navSearchToggle');
    var pop     = document.getElementById('navSearchPopover');
    if (!toggle || !pop) return;
    var input   = document.getElementById('navSearchInput');
    var clearBtn = document.getElementById('navSearchClear');

    function escHtml(s) {
        return (s || '').replace(/[&<>"']/g, function (c) {
            return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
        });
    }

    function getMainColumn() {
        return document.querySelector('.col-main') || document.querySelector('main');
    }
    function getResultsCard() {
        var card = document.getElementById('searchResultsCard');
        if (card) return card;
        var main = getMainColumn();
        if (!main) return null;
        card = document.createElement('div');
        card.id = 'searchResultsCard';
        card.className = 'search-results-card';
        card.style.display = 'none';
        card.innerHTML =
            '<div class="src-head"><h6>Search results</h6>' +
            '<button type="button" class="src-close" aria-label="Close">&times;</button></div>' +
            '<div class="src-body"></div>';
        main.insertBefore(card, main.firstChild);
        card.querySelector('.src-close').addEventListener('click', closeResults);
        return card;
    }

    function positionPopover() {
        var btn = toggle.getBoundingClientRect();
        var vw = window.innerWidth;
        var popWidth = pop.offsetWidth || 380;
        var btnCenter = btn.left + (btn.width / 2);
        var idealLeft = Math.max(8, Math.min(vw - popWidth - 8, btnCenter - (popWidth / 2)));
        pop.style.left = idealLeft + 'px';
        pop.style.transform = 'none';
        pop.style.top = (btn.bottom + 10) + 'px';
        // Position the caret arrow over the button center, relative to the popover
        var caretLeft = btnCenter - idealLeft;
        pop.style.setProperty('--caret-left', caretLeft + 'px');
    }

    function openPopover() {
        pop.style.display = 'block';
        positionPopover();
        setTimeout(function () { input.focus(); }, 0);
    }
    function closePopover() {
        pop.style.display = 'none';
    }
    window.addEventListener('resize', function () {
        if (pop.style.display === 'block') positionPopover();
    });
    function closeResults() {
        var card = document.getElementById('searchResultsCard');
        if (card) { card.style.display = 'none'; card.querySelector('.src-body').innerHTML = ''; }
    }

    toggle.addEventListener('click', function (e) {
        e.preventDefault();
        if (pop.style.display === 'block') closePopover(); else openPopover();
    });
    clearBtn.addEventListener('click', function () {
        input.value = '';
        closeResults();
        closePopover();
    });

    document.addEventListener('click', function (e) {
        if (pop.style.display !== 'block') return;
        if (pop.contains(e.target) || toggle.contains(e.target)) return;
        closePopover();
    });
    document.addEventListener('keydown', function (e) {
        if (e.key === 'Escape') {
            if (pop.style.display === 'block') closePopover();
            else closeResults();
        }
    });

    var t;
    input.addEventListener('input', function () {
        var q = input.value.trim();
        clearTimeout(t);
        if (q.length < 2) { closeResults(); return; }
        t = setTimeout(function () {
            fetch('/Search/Query?q=' + encodeURIComponent(q))
                .then(function (r) { return r.json(); })
                .then(function (data) { render(data, q); })
                .catch(function () {});
        }, 200);
    });

    function section(title) {
        return '<div class="nav-search-section"><div class="nav-search-section-head">' + escHtml(title) + '</div>';
    }

    function render(data, q) {
        var card = getResultsCard();
        if (!card) return;
        var html = '';
        if (data.people && data.people.length) {
            html += section('People');
            data.people.forEach(function (p) {
                var avatar = p.photo
                    ? '<span class="nsr-avatar"><img src="' + escHtml(p.photo) + '" alt=""/></span>'
                    : '<span class="nsr-avatar">' + escHtml((p.name || '?')[0].toUpperCase()) + '</span>';
                html += '<a class="nav-search-row" href="' + escHtml(p.url) + '">' +
                          avatar +
                          '<div class="nsr-body"><div class="nsr-title">' + escHtml(p.name) + '</div>' +
                          '<div class="nsr-meta">@' + escHtml(p.userName) + '</div></div>' +
                        '</a>';
            });
            html += '</div>';
        }
        if (data.posts && data.posts.length) {
            html += section('Posts');
            data.posts.forEach(function (p) {
                html += '<a class="nav-search-row" href="' + escHtml(p.url) + '">' +
                          '<span class="nsr-feature-icon">📜</span>' +
                          '<div class="nsr-body"><div class="nsr-title">' + escHtml(p.title) + ' <span class="nsr-meta">· ' + escHtml(String(p.year)) + ' · ' + escHtml(p.authorName) + '</span></div>' +
                          '<div class="nsr-meta">' + escHtml(p.snippet) + '</div></div>' +
                        '</a>';
            });
            html += '</div>';
        }
        if (data.features && data.features.length) {
            html += section('Features');
            data.features.forEach(function (f) {
                html += '<a class="nav-search-row" href="' + escHtml(f.url) + '">' +
                          '<span class="nsr-feature-icon">›</span>' +
                          '<div class="nsr-body"><div class="nsr-title">' + escHtml(f.name) + '</div>' +
                          '<div class="nsr-meta">' + escHtml(f.hint) + '</div></div>' +
                        '</a>';
            });
            html += '</div>';
        }
        if (!html) html = '<div class="nav-search-empty">No matches for "' + escHtml(q) + '".</div>';
        card.querySelector('.src-body').innerHTML = html;
        card.style.display = 'block';
        card.scrollIntoView({ behavior: 'smooth', block: 'start' });
    }
});

// SignalR presence: 3 states (online / away / offline) + idle detection
// + a window.kronosPresence helper so dynamic content (chat dock,
// dynamically loaded modal lists) can rescan after rendering.
//
// Markers: elements with [data-presence-user="<userId>"]. We toggle
// .is-online (green) or .is-away (amber); no class = offline (grey).
// Users who picked "Look offline" carry data-presence-hidden="true"
// and stay grey regardless of connection state.
(function () {
    if (typeof signalR === 'undefined') return; // hub script only loaded for authenticated users

    var hiddenUsers = new Set();     // user IDs that opted out of showing presence
    var presenceMap = new Map();     // userId → "online" | "away"
    var connection;
    var connectionStarted = false;

    function rescanHiddenUsers() {
        // The chat dock + dynamic modals can add new presence markers
        // after page load. Re-collect data-presence-hidden every time
        // so users who chose "Look offline" stay grey on those rows.
        document.querySelectorAll('[data-presence-user][data-presence-hidden="true"]').forEach(function (el) {
            hiddenUsers.add(el.getAttribute('data-presence-user'));
        });
    }

    function applyTo(userId) {
        var status = presenceMap.get(userId);
        var hidden = hiddenUsers.has(userId);
        var online = !hidden && status === 'online';
        var away   = !hidden && status === 'away';
        document.querySelectorAll('[data-presence-user="' + userId + '"]').forEach(function (el) {
            el.classList.toggle('is-online', online);
            el.classList.toggle('is-away',   away);
        });
    }
    function applyAll() {
        rescanHiddenUsers();
        var seen = new Set();
        document.querySelectorAll('[data-presence-user]').forEach(function (el) {
            var uid = el.getAttribute('data-presence-user');
            seen.add(uid);
        });
        seen.forEach(function (uid) { applyTo(uid); });
    }

    rescanHiddenUsers();

    connection = new signalR.HubConnectionBuilder()
        .withUrl('/hubs/presence')
        .withAutomaticReconnect()
        .build();

    connection.on('PresenceSnapshot', function (map) {
        presenceMap = new Map();
        if (map && typeof map === 'object') {
            Object.keys(map).forEach(function (uid) {
                presenceMap.set(uid, map[uid]);
            });
        }
        applyAll();
    });
    connection.on('PresenceChanged', function (userId, status) {
        if (status === 'offline' || !status) presenceMap.delete(userId);
        else                                 presenceMap.set(userId, status);
        applyTo(userId);
    });

    connection.start().then(function () { connectionStarted = true; }).catch(function () { /* swallow */ });

    // ── Idle → Away ──────────────────────────────────────────────────
    // Fire-and-forget hub call after 5 min of no activity. Any mouse or
    // key event wakes the user back up. SignalR queues the call if the
    // connection isn't ready yet.
    var IDLE_MS = 5 * 60 * 1000;
    var idleTimer = null;
    var isAway = false;
    function setAway(away) {
        if (away === isAway) return;
        isAway = away;
        if (!connectionStarted) return;
        connection.invoke('SetAway', away).catch(function () { /* swallow */ });
    }
    function resetIdleTimer() {
        if (idleTimer) clearTimeout(idleTimer);
        if (isAway) setAway(false);
        idleTimer = setTimeout(function () { setAway(true); }, IDLE_MS);
    }
    ['mousemove', 'mousedown', 'keydown', 'touchstart', 'scroll'].forEach(function (evt) {
        window.addEventListener(evt, resetIdleTimer, { passive: true });
    });
    document.addEventListener('visibilitychange', function () {
        if (document.visibilityState === 'visible') resetIdleTimer();
        else setAway(true);
    });
    resetIdleTimer();

    // ── Public helpers ───────────────────────────────────────────────
    // Used by the chat dock after it (re)renders its conversation list,
    // and by the "Look offline" toggle to flip visibility live.
    window.kronosPresence = {
        rescan: applyAll,
        setVisibility: function (show) {
            if (connectionStarted) {
                connection.invoke('SetVisibility', show).catch(function () { /* swallow */ });
            }
        }
    };
})();

// Reaction picker (heart/thumbs/awesome/I was there/sad) — shared across feed/timeline/detail
document.addEventListener('DOMContentLoaded', function () {
    var token = document.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    var REACTION_ICONS = {
        0: '<svg viewBox="0 0 24 24" fill="currentColor" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M20.84 4.61a5.5 5.5 0 0 0-7.78 0L12 5.67l-1.06-1.06a5.5 5.5 0 0 0-7.78 7.78l1.06 1.06L12 21.23l7.78-7.78 1.06-1.06a5.5 5.5 0 0 0 0-7.78z"/></svg>',
        1: '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M14 9V5a3 3 0 0 0-3-3l-4 9v11h11.28a2 2 0 0 0 2-1.7l1.38-9A2 2 0 0 0 19.66 9H14z"/></svg>',
        2: '<svg viewBox="0 0 24 24" fill="currentColor"><polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2"/></svg>',
        3: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M9 11l3 3 7-7"/><path d="M20 12v6a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2h9"/></svg>',
        4: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"/><path d="M16 16s-1.5-2-4-2-4 2-4 2"/><line x1="9" y1="9" x2="9.01" y2="9"/><line x1="15" y1="9" x2="15.01" y2="9"/></svg>'
    };

    function applyReaction(btn, reaction, count) {
        var iconSlot = btn.querySelector('.reaction-icon');
        var countEl  = btn.querySelector('.like-count');
        if (countEl) countEl.textContent = count;
        if (reaction === null || reaction === undefined) {
            btn.classList.remove('liked');
            btn.dataset.reaction = '0';
            if (iconSlot) iconSlot.innerHTML = REACTION_ICONS[0];
        } else {
            btn.classList.add('liked');
            btn.dataset.reaction = String(reaction);
            if (iconSlot) iconSlot.innerHTML = REACTION_ICONS[reaction] || REACTION_ICONS[0];
        }
    }

    function sendReaction(btn, reactionType) {
        var postId = btn.dataset.postId;
        var fd = new FormData();
        fd.append('reactionType', String(reactionType));
        fetch('/Posts/ToggleLikeAjax/' + postId, {
            method: 'POST',
            headers: { 'X-CSRF-TOKEN': token },
            body: fd
        })
        .then(function (r) { return r.json(); })
        .then(function (data) { applyReaction(btn, data.reaction, data.count); })
        .catch(function () {});
    }

    document.querySelectorAll('.reaction-wrap').forEach(function (wrap) {
        var btn = wrap.querySelector('.like-btn');
        if (!btn) return;
        btn.addEventListener('click', function (e) {
            e.preventDefault();
            e.stopPropagation();
            var current = parseInt(btn.dataset.reaction || '0', 10);
            sendReaction(btn, current);
        });
        wrap.querySelectorAll('.reaction-opt').forEach(function (opt) {
            opt.addEventListener('click', function (e) {
                e.preventDefault();
                e.stopPropagation();
                var rt = parseInt(opt.dataset.reaction || '0', 10);
                sendReaction(btn, rt);
            });
        });
    });
});

// Feed media lightbox
document.addEventListener('DOMContentLoaded', function () {
    var lb       = document.getElementById('kronLightbox');
    if (!lb) return;
    var imgEl    = document.getElementById('kronLightboxImg');
    var vidEl    = document.getElementById('kronLightboxVideo');
    var counter  = document.getElementById('kronLightboxCounter');
    var closeBtn = document.getElementById('kronLightboxClose');
    var prevBtn  = document.getElementById('kronLightboxPrev');
    var nextBtn  = document.getElementById('kronLightboxNext');

    var bubblesL  = document.getElementById('kronLightboxBubblesLeft');
    var bubblesR  = document.getElementById('kronLightboxBubblesRight');
    var commentForm  = document.getElementById('kronLightboxCommentForm');
    var commentInput = document.getElementById('kronLightboxCommentInput');
    var token = function () { return document.querySelector('input[name="__RequestVerificationToken"]')?.value || ''; };

    var items = [];
    var idx = 0;

    function bubble(side, c) {
        var b = document.createElement('div');
        b.className = 'kron-bubble ' + side;
        b.dataset.commentId = c.id;
        var avatar = document.createElement('span');
        avatar.className = 'kron-bubble-avatar';
        if (c.authorPhoto) {
            var img = document.createElement('img');
            img.src = c.authorPhoto;
            avatar.appendChild(img);
        } else {
            avatar.textContent = c.authorInitial || '?';
        }
        var body = document.createElement('div');
        body.className = 'kron-bubble-body';
        var head = document.createElement('div');
        var author = document.createElement('span');
        author.className = 'kron-bubble-author';
        author.textContent = c.authorName || 'Unknown';
        var time = document.createElement('span');
        time.className = 'kron-bubble-time';
        time.textContent = c.createdAt || '';
        head.appendChild(author);
        head.appendChild(time);
        // Delete X — only when the server says canDelete (author or post owner).
        if (c.canDelete) {
            var del = document.createElement('button');
            del.type = 'button';
            del.className = 'kron-bubble-delete';
            del.title = 'Delete this comment';
            del.textContent = '✕';
            del.addEventListener('click', function () {
                if (!confirm('Delete this comment?')) return;
                var fd = new FormData();
                fd.append('__RequestVerificationToken', token());
                fetch('/Media/DeleteComment/' + c.id, {
                    method: 'POST',
                    headers: { 'X-CSRF-TOKEN': token() },
                    body: fd
                }).then(function (r) {
                    if (r.ok) b.remove();
                });
            });
            head.appendChild(del);
        }
        var text = document.createElement('div');
        text.textContent = c.body || '';
        body.appendChild(head);
        body.appendChild(text);
        b.appendChild(avatar);
        b.appendChild(body);
        return b;
    }

    // Face-tag overlays in the lightbox — fetched per-image and injected
    // into the .kron-lightbox-content container so they sit on top of
    // the displayed photo. Clicking a label opens the tagged person.
    function loadLightboxTags(mediaId) {
        var stage = document.querySelector('.kron-lightbox-content');
        if (!stage) return;
        // Wipe any previous overlays.
        stage.querySelectorAll('.kron-lightbox-tag').forEach(function (el) { el.remove(); });
        if (!mediaId) return;
        // Wait until the image has natural dimensions so % positions land
        // on the actual image area rather than the empty container.
        var img = document.getElementById('kronLightboxImg');
        if (!img) return;
        var apply = function (tags) {
            stage.style.position = stage.style.position || 'relative';
            tags.forEach(function (t) {
                var ov = document.createElement('a');
                ov.href = t.href || '#';
                ov.title = (t.isProfile ? '🕊 ' : '') + (t.label || '');
                ov.className = 'kron-lightbox-tag';
                ov.style.cssText = 'position:absolute;left:' + t.x + '%;top:' + t.y + '%;transform:translate(-50%,-50%);z-index:10;text-decoration:none;color:inherit;width:28px;height:28px;display:flex;align-items:center;justify-content:center;';
                ov.innerHTML = '<span style="display:block;width:14px;height:14px;border-radius:50%;background:#fff;border:2px solid #1e7e34;box-shadow:0 0 0 2px rgba(0,0,0,0.45);"></span>'
                             + '<span style="position:absolute;top:30px;left:50%;transform:translateX(-50%);background:rgba(0,0,0,0.78);color:#fff;padding:2px 8px;border-radius:4px;font-size:0.75rem;white-space:nowrap;">'
                             + (t.isProfile ? '🕊 ' : '')
                             + (t.label || '').replace(/[&<>"']/g, function (c) { return { '&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;' }[c]; })
                             + '</span>';
                stage.appendChild(ov);
            });
        };
        fetch('/Media/Tags/' + mediaId)
            .then(function (r) { return r.ok ? r.json() : []; })
            .then(function (tags) {
                if (!Array.isArray(tags) || tags.length === 0) return;
                if (img.complete && img.naturalWidth) { apply(tags); }
                else { img.addEventListener('load', function once() { img.removeEventListener('load', once); apply(tags); }); }
            })
            .catch(function () {});
    }

    function loadComments(mediaId) {
        bubblesL.innerHTML = '';
        bubblesR.innerHTML = '';
        if (!mediaId) return;
        fetch('/Media/Comments/' + mediaId)
            .then(function (r) { return r.json(); })
            .then(function (list) {
                list.forEach(function (c, i) {
                    var b = bubble(i % 2 === 0 ? 'left' : 'right', c);
                    (i % 2 === 0 ? bubblesL : bubblesR).appendChild(b);
                });
            })
            .catch(function () {});
    }

    function open(list, startIdx) {
        items = list;
        idx = startIdx || 0;
        render();
        lb.setAttribute('aria-hidden', 'false');
        document.body.style.overflow = 'hidden';
    }
    function close() {
        lb.setAttribute('aria-hidden', 'true');
        document.body.style.overflow = '';
        vidEl.pause && vidEl.pause();
        vidEl.src = '';
    }
    function render() {
        if (!items.length) return;
        var it = items[idx];
        var isVideo = (it.type || '').toLowerCase() === 'video';
        if (isVideo) {
            imgEl.style.display = 'none';
            imgEl.src = '';
            vidEl.style.display = '';
            vidEl.src = it.url;
        } else {
            vidEl.style.display = 'none';
            vidEl.src = '';
            imgEl.style.display = '';
            imgEl.src = it.url;
        }
        counter.textContent = (idx + 1) + ' / ' + items.length;
        prevBtn.style.visibility = items.length > 1 ? 'visible' : 'hidden';
        nextBtn.style.visibility = items.length > 1 ? 'visible' : 'hidden';
        // Comments + tags only for images (server still allows on videos
        // but bubbles + tag overlays look weird).
        commentForm.style.display = isVideo ? 'none' : '';
        bubblesL.style.display = isVideo ? 'none' : '';
        bubblesR.style.display = isVideo ? 'none' : '';
        loadComments(it.id);
        if (!isVideo) loadLightboxTags(it.id);
    }

    if (commentForm) {
        commentForm.addEventListener('submit', function (e) {
            e.preventDefault();
            var it = items[idx];
            var body = (commentInput.value || '').trim();
            if (!it || !it.id || !body) return;
            var fd = new FormData();
            fd.append('body', body);
            fetch('/Media/AddComment/' + it.id, {
                method: 'POST',
                headers: { 'X-CSRF-TOKEN': token() },
                body: fd
            })
            .then(function (r) { return r.ok ? r.json() : null; })
            .then(function (c) {
                if (!c) return;
                var existing = bubblesL.children.length + bubblesR.children.length;
                var side = existing % 2 === 0 ? 'left' : 'right';
                (side === 'left' ? bubblesL : bubblesR).appendChild(bubble(side, c));
                commentInput.value = '';
            })
            .catch(function () {});
        });
    }
    function step(delta) {
        if (!items.length) return;
        idx = (idx + delta + items.length) % items.length;
        render();
    }

    prevBtn.addEventListener('click', function () { step(-1); });
    nextBtn.addEventListener('click', function () { step(1); });
    closeBtn.addEventListener('click', close);
    lb.addEventListener('click', function (e) { if (e.target === lb) close(); });
    document.addEventListener('keydown', function (e) {
        if (lb.getAttribute('aria-hidden') === 'true') return;
        if (e.key === 'Escape') close();
        else if (e.key === 'ArrowRight') step(1);
        else if (e.key === 'ArrowLeft')  step(-1);
    });

    document.querySelectorAll('.feed-media-grid').forEach(function (grid) {
        var list;
        try { list = JSON.parse(grid.dataset.media || '[]'); } catch (e) { return; }
        grid.querySelectorAll('.feed-media-thumb-btn').forEach(function (btn) {
            btn.addEventListener('click', function (e) {
                e.preventDefault();
                e.stopPropagation();
                var i = parseInt(btn.dataset.index || '0', 10);
                open(list, i);
            });
        });
    });
});

// Quick Story submit with upload progress + success/error feedback
document.addEventListener('DOMContentLoaded', function () {
    document.querySelectorAll('.quick-story-form').forEach(function (form) {
        form.addEventListener('submit', function (ev) {
            ev.preventDefault();
            var submitBtn = form.querySelector('button[type="submit"]');
            if (submitBtn) submitBtn.disabled = true;

            var status = form.querySelector('.quick-upload-status');
            if (!status) {
                status = document.createElement('div');
                status.className = 'quick-upload-status';
                status.innerHTML =
                    '<div class="qup-label"><span class="qup-text">Posting…</span><span class="qup-pct">0%</span></div>' +
                    '<div class="qup-bar"><div class="qup-bar-fill"></div></div>';
                form.appendChild(status);
            }
            status.classList.remove('qup-success', 'qup-error');
            var fill = status.querySelector('.qup-bar-fill');
            var pct  = status.querySelector('.qup-pct');
            var txt  = status.querySelector('.qup-text');
            fill.style.width = '0%';
            pct.textContent = '0%';
            txt.textContent = 'Uploading…';

            var fd = new FormData(form);
            var xhr = new XMLHttpRequest();
            xhr.open(form.method || 'POST', form.action, true);

            xhr.upload.addEventListener('progress', function (e) {
                if (!e.lengthComputable) return;
                var p = Math.round((e.loaded / e.total) * 100);
                fill.style.width = p + '%';
                pct.textContent  = p + '%';
                if (p >= 100) txt.textContent = 'Saving…';
            });
            xhr.onload = function () {
                if (xhr.status >= 200 && xhr.status < 400) {
                    status.classList.add('qup-success');
                    status.querySelector('.qup-bar').style.display = 'none';
                    txt.textContent = '✓ Posted!';
                    pct.textContent = '';
                    setTimeout(function () { window.location.reload(); }, 700);
                } else {
                    status.classList.add('qup-error');
                    txt.textContent = xhr.status === 413
                        ? 'Too big — try fewer or smaller files (250 MB max).'
                        : ('Could not post (status ' + xhr.status + '). Try again.');
                    pct.textContent = '';
                    if (submitBtn) submitBtn.disabled = false;
                }
            };
            xhr.onerror = function () {
                status.classList.add('qup-error');
                txt.textContent = 'Connection lost while uploading. Try again.';
                pct.textContent = '';
                if (submitBtn) submitBtn.disabled = false;
            };
            xhr.send(fd);
        });
    });
});

// Quick Story Memory Music — modal saves a URL into the form's hidden musicUrl field
document.addEventListener('DOMContentLoaded', function () {
    document.querySelectorAll('.music-save-btn').forEach(function (btn) {
        btn.addEventListener('click', function () {
            var formId = btn.dataset.targetForm;
            var form = document.getElementById(formId);
            var modal = btn.closest('.modal');
            if (!form || !modal) return;
            var input = modal.querySelector('.music-url-input');
            var hidden = form.querySelector('.quick-music-url');
            var chip = form.querySelector('.quick-music-chip');
            var badge = form.querySelector('.quick-music-btn .music-set-badge');
            var url = (input.value || '').trim();
            if (hidden) hidden.value = url;
            if (chip) {
                if (url) {
                    chip.style.display = '';
                    chip.innerHTML = '<span class="music-chip-inner">🎵 Memory Music attached <a href="' + url.replace(/"/g, '&quot;') + '" target="_blank" rel="noopener" class="ms-2 small text-decoration-underline">open</a> <button type="button" class="music-chip-remove btn btn-link btn-sm p-0 ms-2">remove</button></span>';
                    var rm = chip.querySelector('.music-chip-remove');
                    if (rm) rm.addEventListener('click', function () {
                        if (hidden) hidden.value = '';
                        chip.style.display = 'none';
                        if (badge) { badge.textContent = ''; badge.classList.add('d-none'); }
                        input.value = '';
                    });
                } else {
                    chip.style.display = 'none';
                }
            }
            if (badge) {
                if (url) { badge.textContent = '•'; badge.classList.remove('d-none'); }
                else     { badge.textContent = '';  badge.classList.add('d-none'); }
            }
        });
    });
});

// Quick Story media attachment preview + count badge
document.addEventListener('DOMContentLoaded', function () {
    document.querySelectorAll('.quick-story-form').forEach(function (form) {
        var preview = form.querySelector('.quick-media-preview');
        var badge = form.querySelector('.media-count-badge');
        if (!preview) return;
        // Modal holds the form-associated inputs for this form
        var formId = form.id;
        var imagesInput = document.querySelector('input.quick-media-images[form="' + formId + '"]');
        var videoInput  = document.querySelector('input.quick-media-video[form="' + formId + '"]');

        function render() {
            preview.innerHTML = '';
            var count = 0;
            if (imagesInput && imagesInput.files) {
                Array.from(imagesInput.files).forEach(function (file) {
                    count++;
                    var img = document.createElement('img');
                    img.className = 'thumb';
                    img.alt = file.name;
                    img.src = URL.createObjectURL(file);
                    preview.appendChild(img);
                });
            }
            if (videoInput && videoInput.files && videoInput.files[0]) {
                count++;
                var ph = document.createElement('div');
                ph.className = 'thumb thumb-video';
                ph.textContent = '▶ ' + videoInput.files[0].name;
                preview.appendChild(ph);
            }
            if (badge) {
                if (count > 0) { badge.textContent = count; badge.classList.remove('d-none'); }
                else { badge.textContent = ''; badge.classList.add('d-none'); }
            }
        }

        if (imagesInput) imagesInput.addEventListener('change', render);
        if (videoInput)  videoInput.addEventListener('change', render);
    });
});

// Inline comments expand/collapse on feed/timeline cards
document.addEventListener('DOMContentLoaded', function () {
    var token = function () { return document.querySelector('input[name="__RequestVerificationToken"]')?.value || ''; };

    function escHtml(s) {
        return (s || '').replace(/[&<>"']/g, function (c) {
            return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
        });
    }

    function renderRow(c) {
        var row = document.createElement('div');
        row.className = 'pci-row';
        var avatar = c.authorPhoto
            ? '<span class="pci-avatar"><img src="' + escHtml(c.authorPhoto) + '" alt=""/></span>'
            : '<span class="pci-avatar">' + escHtml(c.authorInitial || '?') + '</span>';
        row.innerHTML = avatar +
            '<div class="pci-body">' +
                '<div class="pci-meta"><strong>' + escHtml(c.authorName) + '</strong>' +
                '<time>' + escHtml(c.createdAt) + '</time></div>' +
                '<div class="pci-text">' + escHtml(c.body) + '</div>' +
            '</div>';
        return row;
    }

    function renderForm(panel, postId) {
        var form = document.createElement('form');
        form.className = 'pci-form';
        form.innerHTML = '<input type="text" placeholder="Write a comment..." maxlength="1000" required />' +
                         '<button type="submit">Send</button>';
        form.addEventListener('submit', function (e) {
            e.preventDefault();
            e.stopPropagation();
            var input = form.querySelector('input');
            var body = (input.value || '').trim();
            if (!body) return;
            var fd = new FormData();
            fd.append('postId', postId);
            fd.append('body', body);
            fetch('/Posts/AddCommentAjax', {
                method: 'POST',
                headers: { 'X-CSRF-TOKEN': token() },
                body: fd
            })
            .then(function (r) { return r.ok ? r.json() : null; })
            .then(function (c) {
                if (!c) return;
                form.parentNode.insertBefore(renderRow(c), form);
                input.value = '';
                // bump count badge on the toggle button
                var card = panel.closest('.card');
                var badge = card && card.querySelector('.toggle-comments-inline[data-post-id="' + postId + '"] .comment-count');
                if (badge) badge.textContent = (parseInt(badge.textContent, 10) || 0) + 1;
            })
            .catch(function () {});
        });
        return form;
    }

    function loadInto(panel, postId) {
        panel.innerHTML = '<div class="pci-empty">Loading…</div>';
        fetch('/Posts/CommentsAjax/' + postId)
            .then(function (r) { return r.json(); })
            .then(function (list) {
                panel.innerHTML = '';
                var top = list.filter(function (c) { return !c.parentId; });
                if (!top.length) {
                    var empty = document.createElement('div');
                    empty.className = 'pci-empty';
                    empty.textContent = 'No comments yet — be the first.';
                    panel.appendChild(empty);
                } else {
                    top.forEach(function (c) { panel.appendChild(renderRow(c)); });
                }
                panel.appendChild(renderForm(panel, postId));
            })
            .catch(function () {
                panel.innerHTML = '<div class="pci-empty">Could not load comments.</div>';
            });
    }

    function wireToggleComments(scope) {
        (scope || document).querySelectorAll('.toggle-comments-inline').forEach(function (btn) {
            if (btn.dataset.bound === '1') return;
            btn.dataset.bound = '1';
            btn.addEventListener('click', function (e) {
                e.preventDefault();
                e.stopPropagation();
                var postId = btn.dataset.postId;
                var card = btn.closest('.card');
                var panel = card && card.querySelector('.post-comments-inline[data-post-id="' + postId + '"]');
                if (!panel) return;
                var open = panel.style.display !== 'none';
                if (open) {
                    panel.style.display = 'none';
                } else {
                    panel.style.display = 'block';
                    if (!panel.dataset.loaded) {
                        loadInto(panel, postId);
                        panel.dataset.loaded = '1';
                    }
                }
            });
        });
    }
    wireToggleComments(document);
    // Expose so the home-feed pagination can re-bind on appended cards.
    var prevWire = window.kronWireFeedExpanders;
    window.kronWireFeedExpanders = function (scope) {
        wireToggleComments(scope);
        if (prevWire) prevWire(scope);
    };
});

// Feed post expand/collapse — three-dot button or clicking the body text itself
document.addEventListener('DOMContentLoaded', function () {
    function wirePostExpand(scope) {
        (scope || document).querySelectorAll('.btn-expand-post').forEach(function (btn) {
            if (btn.dataset.bound === '1') return;
            btn.dataset.bound = '1';
            var label = btn.querySelector('.btn-expand-label');
            btn.addEventListener('click', function (e) {
                e.preventDefault();
                e.stopPropagation();
                var wrap = btn.closest('.post-expand-wrap');
                if (!wrap) return;
                wrap.classList.toggle('expanded');
                if (label) label.textContent = wrap.classList.contains('expanded') ? 'Read less' : 'Read more';
            });
        });

        (scope || document).querySelectorAll('.post-expand-wrap').forEach(function (wrap) {
            if (wrap.dataset.bound === '1') return;
            wrap.dataset.bound = '1';
            var hasShortLong = !!wrap.querySelector('.post-full-text');
            var articleCollapse = wrap.querySelector('.post-article-collapse');
            var hasArticleCollapse = !!articleCollapse && !wrap.classList.contains('is-fully-shown');
            if (!hasShortLong && !hasArticleCollapse) return;
            function toggle(e) {
                if (e.target.closest('a,button,img')) return;
                var sel = window.getSelection && window.getSelection();
                if (sel && sel.toString().length > 0) return;
                wrap.classList.toggle('expanded');
                var lbl = wrap.querySelector('.btn-expand-label');
                if (lbl) lbl.textContent = wrap.classList.contains('expanded') ? 'Read less' : 'Read more';
            }
            if (hasShortLong) {
                var preview = wrap.querySelector('.post-preview-text');
                var full = wrap.querySelector('.post-full-text');
                if (preview) { preview.style.cursor = 'pointer'; preview.addEventListener('click', toggle); }
                if (full)    { full.style.cursor    = 'pointer'; full.addEventListener('click', toggle); }
            }
            if (hasArticleCollapse) {
                articleCollapse.style.cursor = 'pointer';
                articleCollapse.addEventListener('click', toggle);
            }
        });
    }
    wirePostExpand(document);
    var prev = window.kronWireFeedExpanders;
    window.kronWireFeedExpanders = function (scope) {
        wirePostExpand(scope);
        if (prev) prev(scope);
    };
});

// Clipboard + toast helpers (used by the post Share button)
function kronCopyToClipboard(text) {
    if (navigator.clipboard && navigator.clipboard.writeText) {
        return navigator.clipboard.writeText(text);
    }
    return new Promise(function (resolve, reject) {
        try {
            var ta = document.createElement('textarea');
            ta.value = text;
            ta.setAttribute('readonly', '');
            ta.style.position = 'fixed';
            ta.style.opacity = '0';
            document.body.appendChild(ta);
            ta.select();
            document.execCommand('copy');
            document.body.removeChild(ta);
            resolve();
        } catch (e) { reject(e); }
    });
}
function kronToast(msg) {
    var t = document.createElement('div');
    t.className = 'kron-toast';
    t.textContent = msg;
    document.body.appendChild(t);
    requestAnimationFrame(function () { t.classList.add('visible'); });
    setTimeout(function () {
        t.classList.remove('visible');
        setTimeout(function () { t.remove(); }, 250);
    }, 1800);
}

// Share post: copy an absolute link to /Posts/Detail/{id}. Delegated so it
// picks up cards rendered later (inline comments, ajax refresh, etc.).
document.addEventListener('click', function (e) {
    var btn = e.target.closest('.btn-share-post');
    if (!btn) return;
    e.preventDefault();
    e.stopPropagation();
    var id = btn.dataset.postId;
    if (!id) return;
    var url = window.location.origin + '/Posts/Detail/' + id;
    kronCopyToClipboard(url).then(
        function () { kronToast('Link copied'); },
        function () { kronToast('Couldn’t copy link'); }
    );
});

// Sidebar height management: previous version stripped .sticky-rail on
// tall sidebars so the rail scrolled with the page. Now the rail is
// always sticky and overflows internally (see .col-sidebar.sticky-rail
// CSS), so this no-ops — kept as a hook in case we need conditional
// behavior later (e.g., a user preference).

// Comment edit / delete by the author. Inline edit toggles a textarea that
// replaces the rendered body; Save POSTs to /Posts/EditComment and drops the
// fresh server-rendered HTML back in. Delete confirms then POSTs to
// /Posts/DeleteComment and removes the comment row from the DOM.
document.addEventListener('click', function (e) {
    var row = e.target.closest('.comment-row, .comment-reply');
    if (!row) return;

    function tokenValue() {
        var el = document.querySelector('input[name="__RequestVerificationToken"]');
        return el ? el.value : '';
    }

    if (e.target.closest('.comment-edit-toggle')) {
        e.preventDefault();
        var bodyEl = row.querySelector('.comment-body');
        var formEl = row.querySelector('.comment-edit-form');
        var actionsEl = row.querySelector('.comment-actions');
        if (bodyEl) bodyEl.style.display = 'none';
        if (actionsEl) actionsEl.style.display = 'none';
        if (formEl) {
            formEl.style.display = 'block';
            var ta = formEl.querySelector('.comment-edit-input');
            if (ta) { ta.focus(); ta.setSelectionRange(ta.value.length, ta.value.length); }
        }
        return;
    }

    if (e.target.closest('.comment-edit-cancel')) {
        e.preventDefault();
        var bodyEl2 = row.querySelector('.comment-body');
        var formEl2 = row.querySelector('.comment-edit-form');
        var actionsEl2 = row.querySelector('.comment-actions');
        if (formEl2) formEl2.style.display = 'none';
        if (bodyEl2) bodyEl2.style.display = '';
        if (actionsEl2) actionsEl2.style.display = '';
        return;
    }

    if (e.target.closest('.comment-edit-save')) {
        e.preventDefault();
        var saveBtn = e.target.closest('.comment-edit-save');
        var commentId = row.dataset.commentId;
        if (!commentId) return;
        var ta = row.querySelector('.comment-edit-input');
        var newBody = ta ? ta.value.trim() : '';
        if (!newBody) return;
        saveBtn.disabled = true;
        var fd = new FormData();
        fd.append('id', commentId);
        fd.append('body', newBody);
        var token = tokenValue();
        if (token) fd.append('__RequestVerificationToken', token);
        fetch('/Posts/EditComment', { method: 'POST', body: fd })
            .then(function (r) { return r.ok ? r.json() : Promise.reject(r); })
            .then(function (data) {
                var bodyEl3 = row.querySelector('.comment-body');
                var formEl3 = row.querySelector('.comment-edit-form');
                var actionsEl3 = row.querySelector('.comment-actions');
                if (bodyEl3) {
                    bodyEl3.innerHTML = data.html || '';
                    bodyEl3.style.display = '';
                }
                if (formEl3) formEl3.style.display = 'none';
                if (actionsEl3) actionsEl3.style.display = '';
                if (typeof kronToast === 'function') kronToast('Comment updated');
            })
            .catch(function () {
                if (typeof kronToast === 'function') kronToast('Couldn’t save the edit');
            })
            .finally(function () { saveBtn.disabled = false; });
        return;
    }

    if (e.target.closest('.comment-delete')) {
        e.preventDefault();
        var commentId2 = row.dataset.commentId;
        if (!commentId2) return;
        if (!confirm('Delete this comment? Replies to it will be removed too. This cannot be undone.')) return;
        var fd2 = new FormData();
        fd2.append('id', commentId2);
        var token2 = tokenValue();
        if (token2) fd2.append('__RequestVerificationToken', token2);
        fetch('/Posts/DeleteComment', { method: 'POST', body: fd2 })
            .then(function (r) { return r.ok ? r.json() : Promise.reject(r); })
            .then(function () {
                row.remove();
                if (typeof kronToast === 'function') kronToast('Comment deleted');
            })
            .catch(function () {
                if (typeof kronToast === 'function') kronToast('Couldn’t delete the comment');
            });
        return;
    }
});

// Like / unlike a comment — delegated so it works on replies too.
document.addEventListener('click', function (e) {
    var btn = e.target.closest('.btn-comment-like');
    if (!btn) return;
    e.preventDefault();
    e.stopPropagation();
    var commentId = btn.dataset.commentId;
    if (!commentId) return;
    if (btn.dataset.busy === '1') return;
    btn.dataset.busy = '1';

    var tokenEl = document.querySelector('input[name="__RequestVerificationToken"]');
    var token = tokenEl ? tokenEl.value : '';
    var fd = new FormData();
    if (token) fd.append('__RequestVerificationToken', token);

    fetch('/Posts/LikeComment/' + encodeURIComponent(commentId), { method: 'POST', body: fd })
        .then(function (r) { return r.ok ? r.json() : Promise.reject(r); })
        .then(function (data) {
            btn.classList.toggle('is-liked', !!data.liked);
            var countEl = btn.querySelector('.comment-like-count');
            if (countEl) countEl.textContent = data.count > 0 ? String(data.count) : '';
            btn.setAttribute('aria-label', data.liked ? 'Unlike comment' : 'Like comment');
        })
        .catch(function () {
            if (typeof kronToast === 'function') kronToast('Couldn’t register that');
        })
        .finally(function () { delete btn.dataset.busy; });
});

// Translate post — three entry points:
//   - click the main .btn-translate-post icon → translate to user's default
//     (profile preference; English fallback). Clicking again toggles back.
//   - pick a language from the dropdown (.translate-lang-pick) → translate to
//     that specific language, regardless of default. Picking the active one
//     again toggles back. Picking a different one swaps to that language.
//   - click .translate-show-original in the dropdown → restore originals.
(function () {
    function findBtn(postId) {
        return document.querySelector('.btn-translate-post[data-post-id="' + postId + '"]');
    }
    function findEls(postId) {
        return {
            body:  document.querySelector('[data-translate-body="'  + postId + '"]'),
            title: document.querySelector('[data-translate-title="' + postId + '"]'),
            comments: document.querySelectorAll('[data-translate-comment-body]')
        };
    }
    function savePristine(els) {
        if (els.body && els.body.dataset.original == null)   els.body.dataset.original  = els.body.innerHTML;
        if (els.title && els.title.dataset.original == null) els.title.dataset.original = els.title.textContent;
        els.comments.forEach(function (el) {
            if (el.dataset.original == null) el.dataset.original = el.innerHTML;
        });
    }
    function restore(btn, els) {
        if (els.body && els.body.dataset.original != null) els.body.innerHTML = els.body.dataset.original;
        if (els.title && els.title.dataset.original != null) els.title.textContent = els.title.dataset.original;
        els.comments.forEach(function (el) {
            if (el.dataset.original != null) el.innerHTML = el.dataset.original;
        });
        if (btn) {
            btn.classList.remove('is-translated');
            delete btn.dataset.state;
            delete btn.dataset.activeLang;
        }
    }
    function applyTranslation(btn, els, data, targetLang) {
        if (els.body) els.body.textContent = data.body || '';
        if (els.title && data.title) els.title.textContent = data.title;
        (data.comments || []).forEach(function (c) {
            var el = document.querySelector('[data-translate-comment-body="' + c.id + '"]');
            if (el) el.textContent = c.body || '';
        });
        if (btn) {
            btn.classList.add('is-translated');
            btn.dataset.state = 'translated';
            if (targetLang) btn.dataset.activeLang = targetLang;
            else delete btn.dataset.activeLang;
        }
    }
    function translate(postId, targetLang /* may be undefined = default */) {
        var btn = findBtn(postId);
        var els = findEls(postId);
        if (!els.body) return;

        savePristine(els);
        if (btn) { btn.disabled = true; btn.classList.add('is-loading'); }

        var tokenEl = document.querySelector('input[name="__RequestVerificationToken"]');
        var token = tokenEl ? tokenEl.value : '';
        var fd = new FormData();
        if (token) fd.append('__RequestVerificationToken', token);
        var url = '/Posts/Translate/' + encodeURIComponent(postId);
        if (targetLang) url += '?to=' + encodeURIComponent(targetLang);

        fetch(url, { method: 'POST', body: fd })
            .then(function (r) { return r.ok ? r.json() : r.json().then(function (j) { throw j; }); })
            .then(function (data) {
                applyTranslation(btn, els, data, targetLang);
                if (btn) { btn.classList.remove('is-loading'); btn.disabled = false; }
            })
            .catch(function () {
                if (btn) { btn.classList.remove('is-loading'); btn.disabled = false; }
                if (typeof kronToast === 'function') kronToast('Translation failed');
            });
    }

    // Main icon click: default-language toggle.
    document.addEventListener('click', function (e) {
        var btn = e.target.closest('.btn-translate-post');
        if (!btn) return;
        e.preventDefault();
        e.stopPropagation();
        var postId = btn.dataset.postId;
        if (!postId) return;
        if (btn.dataset.state === 'translated') {
            restore(btn, findEls(postId));
        } else {
            translate(postId);
        }
    });

    // Language pick from dropdown.
    document.addEventListener('click', function (e) {
        var item = e.target.closest('.translate-lang-pick');
        if (!item) return;
        e.preventDefault();
        var postId = item.dataset.postId;
        var to = item.dataset.to;
        if (!postId || !to) return;
        var btn = findBtn(postId);
        // Same language as the currently active one → toggle back.
        if (btn && btn.dataset.state === 'translated' && btn.dataset.activeLang === to) {
            restore(btn, findEls(postId));
            return;
        }
        translate(postId, to);
    });

    // Show original from dropdown.
    document.addEventListener('click', function (e) {
        var item = e.target.closest('.translate-show-original');
        if (!item) return;
        e.preventDefault();
        var postId = item.dataset.postId;
        if (!postId) return;
        restore(findBtn(postId), findEls(postId));
    });
})();

// Sidebar rotator — cycles through prompt + tips/announcements every 5s
document.addEventListener('DOMContentLoaded', function () {
    var rot = document.querySelector('.rail-rotator');
    if (!rot) return;
    var items;
    try { items = JSON.parse(rot.dataset.items || '[]'); } catch (e) { return; }
    if (!items.length) return;

    var badge = rot.querySelector('.rail-rotator-badge');
    var text  = rot.querySelector('.rail-rotator-text');
    var btn   = rot.querySelector('.rail-rotator-btn');
    var idx = 0;

    function fitText() {
        // Shrink font-size until the text fits within the visible row, or hit floor.
        var max = 16;   // px (~1rem)
        var min = 11;   // px (~0.7rem)
        var size = max;
        text.style.fontSize = size + 'px';
        // scrollHeight > clientHeight means it overflows the flex row
        while (text.scrollHeight > text.clientHeight && size > min) {
            size -= 1;
            text.style.fontSize = size + 'px';
        }
    }

    function render() {
        var it = items[idx];
        if (!it) return;
        badge.className = 'tips-badge rail-rotator-badge tips-badge-' + (it.kind || 'tip');
        badge.textContent = it.label || '';
        text.textContent = it.text || '';
        if (it.kind === 'prompt') {
            btn.style.display = 'inline-block';
            btn.setAttribute('href', window.location.pathname === '/'
                ? '#quickStoryFormHome'
                : '/?prompt=' + encodeURIComponent(it.text) + '#quickStoryFormHome');
        } else {
            btn.style.display = 'none';
        }
        // Layout has to settle before we measure, hence rAF
        requestAnimationFrame(fitText);
    }

    function tick() {
        rot.classList.add('rail-rotator-fading');
        setTimeout(function () {
            idx = (idx + 1) % items.length;
            render();
            rot.classList.remove('rail-rotator-fading');
        }, 220);
    }

    render();
    if (items.length > 1) setInterval(tick, 5000);
});

// Scroll to + focus Quick Story when URL has #quickStoryFormHome / ?prompt=,
// or when any in-page link points at the form (e.g. the rail's New Story button)
document.addEventListener('DOMContentLoaded', function () {
    function focusQuickStory() {
        var form = document.getElementById('quickStoryFormHome')
                || document.getElementById('quickStoryFormTimeline');
        if (!form) return false;
        form.scrollIntoView({ behavior: 'smooth', block: 'start' });
        var ta = form.querySelector('.quick-story-textarea');
        if (ta) ta.focus();
        return true;
    }

    // On initial load
    var hash = window.location.hash || '';
    var hasPromptParam = new URLSearchParams(window.location.search).has('prompt');
    if (hash === '#quickStoryFormHome' || hasPromptParam) {
        setTimeout(focusQuickStory, 60);
    }

    // On in-page link clicks pointing at the Quick Story form
    document.addEventListener('click', function (e) {
        var a = e.target.closest('a[href$="#quickStoryFormHome"]');
        if (!a) return;
        if (focusQuickStory()) e.preventDefault();
    });
});

// Rotating memory prompt placeholders on Quick Story textarea
document.addEventListener('DOMContentLoaded', function () {
    document.querySelectorAll('.rotating-prompt').forEach(function (ta) {
        var prompts;
        try { prompts = JSON.parse(ta.dataset.prompts || '[]'); } catch (e) { return; }
        if (!prompts || !prompts.length) return;
        var idx = Math.floor(Math.random() * prompts.length);
        ta.placeholder = prompts[idx];
        setInterval(function () {
            if (document.activeElement === ta || ta.value) return;
            idx = (idx + 1) % prompts.length;
            ta.placeholder = prompts[idx];
        }, 6000);
    });
});

// Auto-grow Quick Story textareas (start 1.5 lines, expand as user types)
document.addEventListener('DOMContentLoaded', function () {
    document.querySelectorAll('.quick-story-textarea').forEach(function (ta) {
        function grow() {
            ta.style.height = 'auto';
            ta.style.height = ta.scrollHeight + 'px';
        }
        ta.addEventListener('input', grow);
        grow();
    });
});

// Quick Story inline tag autocomplete
document.addEventListener('DOMContentLoaded', function () {
    document.querySelectorAll('.quick-tag-form').forEach(function (form) {
        var friends = [];
        try { friends = JSON.parse(form.dataset.friends || '[]'); } catch (e) {}
        var input   = form.querySelector('.quick-tag-input');
        var dropdown = form.querySelector('.quick-tag-dropdown');
        var bubblesEl = form.querySelector('.quick-tag-bubbles');
        var hiddenEl  = form.querySelector('.quick-tag-hidden');
        if (!input || !dropdown || !bubblesEl || !hiddenEl) return;
        var selected = {};

        function hideDropdown() { dropdown.style.display = 'none'; }

        input.addEventListener('input', function () {
            var q = this.value.trim().toLowerCase();
            dropdown.innerHTML = '';
            if (q.length < 1) { hideDropdown(); return; }
            var matches = friends.filter(function (f) {
                return f.displayName.toLowerCase().includes(q) && !selected[f.userId];
            });
            if (!matches.length) { hideDropdown(); return; }
            matches.slice(0, 8).forEach(function (f) {
                var btn = document.createElement('button');
                btn.type = 'button';
                btn.className = 'list-group-item list-group-item-action py-1 px-2 d-flex align-items-center gap-2';
                var avatar = document.createElement('span');
                avatar.style.cssText = 'width:22px;height:22px;border-radius:50%;background:var(--mst-light);color:var(--mst-primary);font-weight:bold;font-size:11px;display:inline-flex;align-items:center;justify-content:center;flex-shrink:0;';
                avatar.textContent = f.displayName.trim()[0].toUpperCase();
                btn.appendChild(avatar);
                var name = document.createElement('span');
                name.className = 'small';
                name.textContent = f.displayName;
                btn.appendChild(name);
                btn.addEventListener('mousedown', function (e) {
                    e.preventDefault();
                    addTag(f.userId, f.displayName);
                    input.value = '';
                    hideDropdown();
                });
                dropdown.appendChild(btn);
            });
            dropdown.style.display = 'block';
        });

        input.addEventListener('keydown', function (e) {
            if (e.key === 'Enter') {
                var first = dropdown.querySelector('button');
                if (first && dropdown.style.display === 'block') {
                    e.preventDefault();
                    first.dispatchEvent(new MouseEvent('mousedown'));
                }
            } else if (e.key === 'Backspace' && input.value === '') {
                var last = bubblesEl.querySelector('.tag-bubble:last-child');
                if (last) removeTag(last.dataset.id);
            }
        });

        input.addEventListener('blur', function () {
            setTimeout(hideDropdown, 150);
        });

        function addTag(userId, name) {
            if (selected[userId]) return;
            selected[userId] = name;
            var bubble = document.createElement('span');
            bubble.className = 'tag-bubble';
            bubble.dataset.id = userId;
            var avatar = document.createElement('span');
            avatar.style.cssText = 'width:18px;height:18px;border-radius:50%;background:var(--mst-primary);color:#fff;font-weight:bold;font-size:10px;display:inline-flex;align-items:center;justify-content:center;margin-right:4px;';
            avatar.textContent = name.trim()[0].toUpperCase();
            bubble.appendChild(avatar);
            bubble.appendChild(document.createTextNode(name));
            var removeBtn = document.createElement('button');
            removeBtn.type = 'button';
            removeBtn.innerHTML = '&times;';
            removeBtn.addEventListener('click', function () { removeTag(userId); });
            bubble.appendChild(removeBtn);
            bubblesEl.appendChild(bubble);

            var hidden = document.createElement('input');
            hidden.type = 'hidden';
            hidden.name = 'TaggedUserIds';
            hidden.value = userId;
            hidden.dataset.bubbleFor = userId;
            hiddenEl.appendChild(hidden);
        }

        function removeTag(userId) {
            delete selected[userId];
            var b = bubblesEl.querySelector('[data-id="' + userId + '"]');
            if (b) b.remove();
            var h = hiddenEl.querySelector('input[data-bubble-for="' + userId + '"]');
            if (h) h.remove();
        }
    });
});

// Auto-dismiss alerts after 5 seconds
document.addEventListener('DOMContentLoaded', function () {
    var alerts = document.querySelectorAll('.alert-dismissible');
    alerts.forEach(function (alert) {
        setTimeout(function () {
            var bsAlert = bootstrap.Alert.getOrCreateInstance(alert);
            if (bsAlert) bsAlert.close();
        }, 5000);
    });

    // Tier selector: set the correct selected option
    document.querySelectorAll('select[name="tier"]').forEach(function (select) {
        var form = select.closest('form');
        if (form) {
            var currentVal = select.getAttribute('data-current');
            if (currentVal) {
                select.value = currentVal;
            }
        }
    });

    // Apply saved image focal points (object-position) from localStorage
    document.querySelectorAll('.post-media-wrap img').forEach(function (el) {
        var saved = el.src ? localStorage.getItem('imgfocus:' + el.src) : null;
        if (saved) {
            try {
                var p = JSON.parse(saved);
                el.style.objectPosition = 'calc(50% + ' + p.x + 'px) calc(50% + ' + p.y + 'px)';
            } catch (e) {}
        }
    });

    // Attach image modal triggers
    initImageModal();
});

// ── Image viewer modal + focus editor (owner only) ───────────────────────────
function initImageModal() {
    var currentUserId = (document.querySelector('meta[name="current-user-id"]') || {}).content || '';

    var modal = null, modalImg = null;
    var focusOverlay = null;
    var currentSrc = '', currentOwner = '';

    function createModal() {
        modal = document.createElement('div');
        modal.style.cssText = 'display:none;position:fixed;inset:0;z-index:9999;background:rgba(0,0,0,0.88);justify-content:center;align-items:center;';

        var inner = document.createElement('div');
        inner.style.cssText = 'position:relative;display:inline-block;max-width:92vw;max-height:92vh;';

        modalImg = document.createElement('img');
        modalImg.style.cssText = 'display:block;max-width:92vw;max-height:92vh;border-radius:4px;';

        // Close button
        var closeBtn = document.createElement('button');
        closeBtn.innerHTML = '✕';
        closeBtn.style.cssText = 'position:absolute;top:8px;right:8px;background:rgba(0,0,0,0.55);color:#fff;border:none;border-radius:50%;width:30px;height:30px;font-size:15px;cursor:pointer;z-index:2;line-height:1;';
        closeBtn.addEventListener('click', closeModal);

        // "Change Focus" button — only shown for post owner
        var focusBtn = document.createElement('button');
        focusBtn.innerHTML = '🎯 Change Focus';
        focusBtn.id = 'mst-focus-btn';
        focusBtn.style.cssText = 'position:absolute;bottom:10px;right:10px;background:rgba(0,0,0,0.6);color:#fff;border:none;border-radius:6px;padding:5px 12px;font-size:12px;cursor:pointer;z-index:2;display:none;';
        focusBtn.addEventListener('click', function () {
            closeModal();
            openFocusEditor(currentSrc);
        });

        inner.appendChild(modalImg);
        inner.appendChild(closeBtn);
        inner.appendChild(focusBtn);
        modal.appendChild(inner);
        document.body.appendChild(modal);

        modal.addEventListener('click', function (e) { if (e.target === modal) closeModal(); });
        document.addEventListener('keydown', function (e) { if (e.key === 'Escape') { closeModal(); closeFocusEditor(); } });
    }

    function openModal(src, ownerId) {
        if (!modal) createModal();
        currentSrc = src;
        currentOwner = ownerId || '';
        modalImg.src = src;
        var focusBtn = document.getElementById('mst-focus-btn');
        if (focusBtn) {
            focusBtn.style.display = (currentUserId && currentOwner && currentUserId === currentOwner) ? '' : 'none';
        }
        modal.style.display = 'flex';
        document.body.style.overflow = 'hidden';
    }

    function closeModal() {
        if (modal) modal.style.display = 'none';
        document.body.style.overflow = '';
    }

    // ── Focus editor ─────────────────────────────────────────────────────────
    function createFocusEditor() {
        focusOverlay = document.createElement('div');
        focusOverlay.style.cssText = 'display:none;position:fixed;inset:0;z-index:9999;background:rgba(0,0,0,0.82);justify-content:center;align-items:center;flex-direction:column;gap:12px;';

        var label = document.createElement('div');
        label.innerHTML = 'Drag to set the focal point for the thumbnail';
        label.style.cssText = 'color:#fff;font-size:13px;opacity:0.8;';

        // Crop preview box — 16:9, same proportion as post-media-wrap
        var boxW = Math.min(480, window.innerWidth - 48);
        var boxH = Math.round(boxW * 9 / 16);

        var cropBox = document.createElement('div');
        cropBox.style.cssText = 'position:relative;width:' + boxW + 'px;height:' + boxH + 'px;overflow:hidden;border-radius:8px;border:2px solid var(--mst-gold, #c9a227);cursor:grab;user-select:none;background:#000;';

        var focusImg = document.createElement('img');
        focusImg.style.cssText = 'position:absolute;top:0;left:0;width:100%;height:100%;object-fit:cover;pointer-events:none;';

        var hint = document.createElement('div');
        hint.innerHTML = '← drag →';
        hint.style.cssText = 'position:absolute;bottom:6px;left:50%;transform:translateX(-50%);background:rgba(0,0,0,0.5);color:rgba(255,255,255,0.75);border-radius:4px;padding:2px 8px;font-size:11px;pointer-events:none;';

        cropBox.appendChild(focusImg);
        cropBox.appendChild(hint);

        // drag state
        var isDragging = false, startX, startY, tx = 0, ty = 0;

        function clamp(val, max) { return Math.max(-max, Math.min(max, val)); }

        function applyTransform() {
            focusImg.style.objectPosition = 'calc(50% + ' + tx + 'px) calc(50% + ' + ty + 'px)';
        }

        cropBox.addEventListener('mousedown', function (e) {
            isDragging = true; startX = e.clientX - tx; startY = e.clientY - ty;
            cropBox.style.cursor = 'grabbing'; e.preventDefault();
        });
        document.addEventListener('mousemove', function (e) {
            if (!isDragging) return;
            tx = clamp(e.clientX - startX, boxW / 2);
            ty = clamp(e.clientY - startY, boxH / 2);
            applyTransform();
        });
        document.addEventListener('mouseup', function () {
            if (isDragging) { isDragging = false; cropBox.style.cursor = 'grab'; }
        });
        cropBox.addEventListener('touchstart', function (e) {
            if (e.touches.length !== 1) return;
            isDragging = true; startX = e.touches[0].clientX - tx; startY = e.touches[0].clientY - ty;
            e.preventDefault();
        }, { passive: false });
        cropBox.addEventListener('touchmove', function (e) {
            if (!isDragging || e.touches.length !== 1) return;
            tx = clamp(e.touches[0].clientX - startX, boxW / 2);
            ty = clamp(e.touches[0].clientY - startY, boxH / 2);
            applyTransform(); e.preventDefault();
        }, { passive: false });
        cropBox.addEventListener('touchend', function () { isDragging = false; });

        // Buttons row
        var btns = document.createElement('div');
        btns.style.cssText = 'display:flex;gap:10px;';

        var saveBtn = document.createElement('button');
        saveBtn.innerHTML = '💾 Save Focus';
        saveBtn.style.cssText = 'background:var(--mst-primary,#1e4d2e);color:#fff;border:none;border-radius:6px;padding:7px 18px;font-size:13px;cursor:pointer;';
        saveBtn.addEventListener('click', function () {
            localStorage.setItem('imgfocus:' + currentSrc, JSON.stringify({ x: tx, y: ty }));
            // Update all matching thumbnails on the page
            document.querySelectorAll('.post-media-wrap img[src="' + currentSrc + '"]').forEach(function (el) {
                el.style.objectPosition = 'calc(50% + ' + tx + 'px) calc(50% + ' + ty + 'px)';
            });
            saveBtn.innerHTML = '✔ Saved!';
            setTimeout(function () { saveBtn.innerHTML = '💾 Save Focus'; }, 1500);
        });

        var cancelBtn = document.createElement('button');
        cancelBtn.innerHTML = 'Cancel';
        cancelBtn.style.cssText = 'background:rgba(255,255,255,0.15);color:#fff;border:none;border-radius:6px;padding:7px 18px;font-size:13px;cursor:pointer;';
        cancelBtn.addEventListener('click', closeFocusEditor);

        btns.appendChild(saveBtn);
        btns.appendChild(cancelBtn);

        focusOverlay.appendChild(label);
        focusOverlay.appendChild(cropBox);
        focusOverlay.appendChild(btns);
        document.body.appendChild(focusOverlay);

        // Store refs for openFocusEditor
        focusOverlay._focusImg = focusImg;
        focusOverlay._resetState = function (src) {
            var saved = localStorage.getItem('imgfocus:' + src);
            tx = 0; ty = 0;
            if (saved) { try { var p = JSON.parse(saved); tx = p.x; ty = p.y; } catch (e) {} }
            focusImg.src = src;
            applyTransform();
        };
    }

    function openFocusEditor(src) {
        if (!focusOverlay) createFocusEditor();
        currentSrc = src;
        focusOverlay._resetState(src);
        focusOverlay.style.display = 'flex';
        document.body.style.overflow = 'hidden';
    }

    function closeFocusEditor() {
        if (focusOverlay) focusOverlay.style.display = 'none';
        document.body.style.overflow = '';
    }

    // Attach click handlers to all post media images
    document.querySelectorAll('.post-media-wrap img').forEach(function (el) {
        el.style.cursor = 'zoom-in';
        el.addEventListener('click', function (e) {
            e.preventDefault();
            e.stopPropagation();
            var wrap = el.closest('.post-media-wrap');
            var ownerId = wrap ? (wrap.getAttribute('data-owner-id') || '') : '';
            openModal(el.src, ownerId);
        });
    });
}

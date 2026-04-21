// Multi-page guided product tour. State lives in localStorage.kronTourStep_v1.
// Starts only when explicitly triggered (menu link with .start-product-tour);
// never auto-starts on first visit. Skip from any step ends the tour.
(function () {
    var STEP_KEY = 'kronTourStep_v1';

    var STEPS = [
        {
            id: 1, path: '/', anchor: '[data-tour="quick-story"]',
            title: 'Quick Story',
            body: 'Jot a quick memory right here — a sentence, a photo, a year, done. Great for fragments you don\'t want to lose.'
        },
        {
            id: 2, path: '/', anchor: '[data-tour="full-story-btn"]',
            title: 'Full Story',
            body: 'For longer memories that deserve a title, a date, photos, and memory music, open a Full Story. You can save it as a draft anytime.'
        },
        {
            id: 3, path: '/', anchor: '[data-tour="visibility"]',
            title: 'Who sees each story',
            body: 'Every post has an audience: Public, Friends, Family, Acquaintances, or only you. This maps to the tiers in your network — which is our next stop.'
        },
        {
            id: 4, path: '/Friends', anchor: '[data-tour="network-tiers"]',
            title: 'Your network, in tiers',
            body: 'Organize people into Acquaintances, Friends, and Family. These are the same tiers you picked from when setting visibility — that\'s how you decide who sees what. Use the search above to add people, or send a link invite from Home.'
        },
        {
            id: 5, path: '/Posts/Timeline', anchor: '[data-tour="timeline"]',
            title: 'My Story — your timeline',
            body: 'Everything you publish lands here in the order it happened. Your life, chronologically, at a glance. Zoom by decade, year, or month.'
        },
        {
            id: 6, path: '/', anchor: '[data-tour="engage-bar"]',
            title: 'React, reply, share',
            body: 'Under every post you\'ll find reactions (heart, I-was-there, awesome…), a comment field, and a Share button that copies a direct link to the story.'
        },
        {
            id: 7, path: null, anchor: '[data-tour="user-menu"]', openDropdown: true,
            title: 'Everything else lives here',
            body: 'Your profile, drafts, an export of your whole story, and the link to replay this tour — all in this menu. That\'s it for the tour.'
        }
    ];

    function visible(selector) {
        var els = document.querySelectorAll(selector);
        for (var i = 0; i < els.length; i++) {
            if (els[i].offsetParent !== null) return els[i];
        }
        return null;
    }

    function currentStep() {
        var n = parseInt(localStorage.getItem(STEP_KEY), 10);
        if (!n) return null;
        for (var i = 0; i < STEPS.length; i++) { if (STEPS[i].id === n) return STEPS[i]; }
        return null;
    }

    function pathMatches(target) {
        if (!target) return true;
        var p = window.location.pathname;
        p = p === '/' ? '/' : p.replace(/\/$/, '');
        if (target === '/') {
            return p === '/' || p === '/Home' || p === '/Home/Index';
        }
        if (p === target) return true;
        if (p === target + '/Index') return true;
        if (p.indexOf(target + '/') === 0) return true;
        return false;
    }

    function resolvePath(step) {
        if (!step.path) return null;
        if (step.path === '/Posts/Timeline') {
            var m = document.querySelector('meta[name="current-user-id"]');
            var uid = m ? m.getAttribute('content') : null;
            return uid ? '/Posts/Timeline/' + uid : step.path;
        }
        return step.path;
    }

    function start() {
        localStorage.setItem(STEP_KEY, '1');
        var first = STEPS[0];
        if (pathMatches(first.path)) {
            render(first);
        } else {
            window.location.href = resolvePath(first) || '/';
        }
    }

    function skip() {
        localStorage.removeItem(STEP_KEY);
        teardown();
    }

    function next() {
        var s = currentStep();
        if (!s) return;
        var idx = STEPS.indexOf(s);
        if (idx === STEPS.length - 1) { skip(); return; }
        var nx = STEPS[idx + 1];
        localStorage.setItem(STEP_KEY, String(nx.id));
        if (nx.path && !pathMatches(nx.path)) {
            window.location.href = resolvePath(nx);
        } else {
            teardown();
            render(nx);
        }
    }

    var currentHighlight = null;

    function clearHighlight() {
        if (currentHighlight) {
            currentHighlight.classList.remove('kron-tour-highlight');
            currentHighlight = null;
        }
    }

    function teardown() {
        var overlay = document.getElementById('kronTourOverlay');
        var popup = document.getElementById('kronTourPopup');
        if (overlay) overlay.style.display = 'none';
        if (popup) popup.style.display = 'none';
        clearHighlight();
        document.body.classList.remove('kron-tour-active');
    }

    function positionPopup(popup, anchor) {
        var r = anchor.getBoundingClientRect();
        var pw = popup.offsetWidth;
        var ph = popup.offsetHeight;
        var margin = 12;
        var vw = window.innerWidth;
        var vh = window.innerHeight;

        // prefer below; if not enough room, place above; if still no room, to the side
        var top, left;
        if (r.bottom + ph + margin < vh) {
            top = r.bottom + margin;
        } else if (r.top - ph - margin > 0) {
            top = r.top - ph - margin;
        } else {
            top = Math.max(margin, (vh - ph) / 2);
        }
        left = r.left + (r.width / 2) - (pw / 2);
        left = Math.max(margin, Math.min(left, vw - pw - margin));

        popup.style.position = 'fixed';
        popup.style.top = top + 'px';
        popup.style.left = left + 'px';
        popup.style.transform = '';
    }

    function render(step) {
        var overlay = document.getElementById('kronTourOverlay');
        var popup = document.getElementById('kronTourPopup');
        if (!overlay || !popup) return;

        if (step.openDropdown) {
            // Open the user dropdown so it's visible for the spotlight.
            var toggle = visible('.navbar-nav .dropdown-toggle.nav-link-gold');
            if (toggle && window.bootstrap) {
                try { bootstrap.Dropdown.getOrCreateInstance(toggle).show(); } catch (e) {}
            }
        }

        popup.querySelector('.kron-tour-counter').textContent = 'Step ' + step.id + ' of ' + STEPS.length;
        popup.querySelector('.kron-tour-title').textContent = step.title;
        popup.querySelector('.kron-tour-body').textContent = step.body;
        popup.querySelector('.kron-tour-next').textContent = (step.id === STEPS.length) ? 'Finish' : 'Next';

        overlay.style.display = 'block';
        popup.style.display = 'block';
        document.body.classList.add('kron-tour-active');

        clearHighlight();
        var anchor = step.anchor ? visible(step.anchor) : null;
        if (anchor) {
            anchor.classList.add('kron-tour-highlight');
            currentHighlight = anchor;
            // scroll into view if offscreen
            var r = anchor.getBoundingClientRect();
            if (r.top < 0 || r.bottom > window.innerHeight) {
                anchor.scrollIntoView({ behavior: 'smooth', block: 'center' });
                setTimeout(function () { positionPopup(popup, anchor); }, 300);
            } else {
                positionPopup(popup, anchor);
            }
        } else {
            popup.style.position = 'fixed';
            popup.style.top = '50%';
            popup.style.left = '50%';
            popup.style.transform = 'translate(-50%, -50%)';
        }
    }

    function wire() {
        document.querySelectorAll('.start-product-tour').forEach(function (el) {
            el.addEventListener('click', function (e) {
                e.preventDefault();
                start();
            });
        });
        var popup = document.getElementById('kronTourPopup');
        if (popup) {
            popup.addEventListener('click', function (e) {
                if (e.target.closest('.kron-tour-next')) { next(); }
                else if (e.target.closest('.kron-tour-skip')) { skip(); }
            });
        }
        document.addEventListener('keydown', function (e) {
            if (!currentStep()) return;
            if (e.key === 'Escape') skip();
        });
    }

    function resume() {
        var s = currentStep();
        if (!s) return;
        if (!pathMatches(s.path)) return; // dormant on unrelated pages
        // small delay so Bootstrap dropdowns, CSS, images are settled
        setTimeout(function () { render(s); }, 250);
    }

    document.addEventListener('DOMContentLoaded', function () {
        wire();
        resume();
    });

    // reposition popup on resize/scroll so it stays attached to the anchor
    window.addEventListener('resize', function () {
        if (currentHighlight) {
            var popup = document.getElementById('kronTourPopup');
            if (popup && popup.style.display !== 'none') positionPopup(popup, currentHighlight);
        }
    });
})();

// Multi-page guided product tour. State lives in localStorage:
//   kronTourStep_v1       — current step id while a tour is in progress
//   kronTourDismissed_v2  — sticky flag: tour finished or skipped, don't auto-fire
// Auto-fires on the home feed for any authenticated user who hasn't dismissed
// it yet. Replay link in the user menu re-runs it. Skip/Esc/Finish all set
// the dismissed flag so we never bother the user again.
(function () {
    var STEP_KEY = 'kronTourStep_v1';
    var DISMISSED_KEY = 'kronTourDismissed_v2';
    var KOFI_HANDLE_META = 'kofi-handle';

    // Anchor selectors and routing stay in JS (they reference DOM); only
    // the human-readable copy gets pulled from window.kronStrings.tour.steps,
    // which the layout populates server-side based on the active culture.
    // English is the inline fallback so the tour still works if the strings
    // bundle is missing.
    var STEP_LAYOUT = [
        { id: 1, path: '/',                anchor: null },
        { id: 2, path: '/',                anchor: '[data-tour="quick-story"]' },
        { id: 3, path: '/',                anchor: '[data-tour="visibility"]' },
        { id: 4, path: '/Friends',         anchor: '[data-tour="network-tiers"]' },
        { id: 5, path: '/Posts/Timeline',  anchor: '[data-tour="timeline"]' },
        { id: 6, path: '/',                anchor: '[data-tour="user-menu"]', openDropdown: true },
        { id: 7, path: '/',                anchor: '[data-tour="kofi"]', isFinal: true }
    ];
    var DEFAULT_COPY = [
        { id: 1, title: 'Welcome to Kronoscript', body: 'Kronoscript is unlike other social networks: posts are organized by when they happened, not when they were posted. Two minutes to see what makes it different.' },
        { id: 2, title: 'Two ways to write', body: 'Drop a Quick Story right here for a fragment you don\'t want to lose, or click "Full Story" for a longer memory with a title, date, photos, and memory music. Save as draft anytime.' },
        { id: 3, title: 'Pick who sees each story', body: 'Every post has its own audience: Public, Friends, Family, Acquaintances, or Only You. The tiers match how you organize people in your network — that\'s the next stop.' },
        { id: 4, title: 'Your network, in tiers', body: 'Sort people into Acquaintances, Friends, and Family. Those tiers are exactly what you pick from when setting visibility on a post — that\'s how you control who sees what.' },
        { id: 5, title: 'My Story — your life, chronologically', body: 'Everything you publish lands here in the order it happened, not when you typed it. Zoom by decade, year, or month. Eventually, this is your book.' },
        { id: 6, title: 'Profile, drafts, export, replay', body: 'Settings, your drafts, an export of your whole story, and the link to replay this tour all live behind your name in the top-right.' },
        { id: 7, title: 'Tip the creator — keep it ad-free', body: 'Kronoscript is free and ad-free, supported by tips from people like you. If it helps you tell your story, a small tip on Ko-fi is what keeps the lights on. ☕' }
    ];
    function localizedSteps() {
        var bundle = (window.kronStrings && window.kronStrings.tour) || {};
        var copy = (bundle.steps && bundle.steps.length) ? bundle.steps : DEFAULT_COPY;
        return STEP_LAYOUT.map(function (lay, i) {
            var src = copy[i] || DEFAULT_COPY[i];
            return Object.assign({}, lay, { title: src.title, body: src.body });
        });
    }
    var STEPS = localizedSteps();

    function visible(selector) {
        var els = document.querySelectorAll(selector);
        for (var i = 0; i < els.length; i++) {
            if (els[i].offsetParent !== null) return els[i];
        }
        return null;
    }

    function isAuthenticated() {
        return !!document.querySelector('meta[name="current-user-id"]');
    }

    function isDismissed() {
        return localStorage.getItem(DISMISSED_KEY) === '1';
    }

    function dismissForever() {
        localStorage.setItem(DISMISSED_KEY, '1');
        localStorage.removeItem(STEP_KEY);
    }

    function kofiUrl() {
        var m = document.querySelector('meta[name="' + KOFI_HANDLE_META + '"]');
        var handle = m ? m.getAttribute('content') : null;
        return handle ? 'https://ko-fi.com/' + handle : 'https://ko-fi.com';
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
        // Restart clears the dismiss flag so the user can replay end-to-end.
        localStorage.removeItem(DISMISSED_KEY);
        localStorage.setItem(STEP_KEY, '1');
        var first = STEPS[0];
        if (pathMatches(first.path)) {
            render(first);
        } else {
            window.location.href = resolvePath(first) || '/';
        }
    }

    function skip() {
        dismissForever();
        teardown();
    }

    function next() {
        var s = currentStep();
        if (!s) return;
        var idx = STEPS.indexOf(s);
        if (idx === STEPS.length - 1) {
            // Final step — open Ko-fi in a new tab and dismiss.
            window.open(kofiUrl(), '_blank', 'noopener');
            dismissForever();
            teardown();
            return;
        }
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
            var toggle = visible('.navbar-nav .dropdown-toggle.nav-link-gold');
            if (toggle && window.bootstrap) {
                try { bootstrap.Dropdown.getOrCreateInstance(toggle).show(); } catch (e) {}
            }
        }

        var L = (window.kronStrings && window.kronStrings.tour) || {};
        var counterTpl = L.stepCounter || 'Step {0} of {1}';
        popup.querySelector('.kron-tour-counter').textContent =
            counterTpl.replace('{0}', step.id).replace('{1}', STEPS.length);
        popup.querySelector('.kron-tour-title').textContent = step.title;
        popup.querySelector('.kron-tour-body').textContent = step.body;
        var nextBtn = popup.querySelector('.kron-tour-next');
        var skipBtn = popup.querySelector('.kron-tour-skip');
        if (step.isFinal) {
            nextBtn.textContent = L.tipCreator || 'Tip the creator ☕';
            nextBtn.classList.add('btn-warning');
            nextBtn.classList.remove('btn-primary');
            if (skipBtn) skipBtn.textContent = L.maybeLater || 'Maybe later';
        } else {
            nextBtn.textContent = (step.id === STEPS.length) ? (L.finish || 'Finish') : (L.next || 'Next');
            nextBtn.classList.add('btn-primary');
            nextBtn.classList.remove('btn-warning');
            if (skipBtn) skipBtn.textContent = L.skip || 'Skip tour';
        }

        overlay.style.display = 'block';
        popup.style.display = 'block';
        document.body.classList.add('kron-tour-active');

        clearHighlight();
        var anchor = step.anchor ? visible(step.anchor) : null;
        if (anchor) {
            anchor.classList.add('kron-tour-highlight');
            currentHighlight = anchor;
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

    function autoStartIfFirstTime() {
        if (currentStep()) return; // already mid-tour; resume() handles it
        if (isDismissed()) return;
        if (!isAuthenticated()) return;
        // Only auto-fire on the home feed so we don't surprise users mid-task.
        if (!pathMatches('/')) return;
        // tiny delay so the page paints first
        setTimeout(start, 600);
    }

    function resume() {
        var s = currentStep();
        if (!s) { autoStartIfFirstTime(); return; }
        if (!pathMatches(s.path)) return; // dormant on unrelated pages
        setTimeout(function () { render(s); }, 250);
    }

    document.addEventListener('DOMContentLoaded', function () {
        wire();
        resume();
    });

    window.addEventListener('resize', function () {
        if (currentHighlight) {
            var popup = document.getElementById('kronTourPopup');
            if (popup && popup.style.display !== 'none') positionPopup(popup, currentHighlight);
        }
    });
})();

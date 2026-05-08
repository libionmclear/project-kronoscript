// Lightweight user-search typeahead. Wires onto every
// .user-typeahead-wrap element on the page; queries /Admin/SearchUsers as
// the admin types and offers up to 10 suggestions. Click or arrow-key to
// fill the field with the matching username.
(function () {
    var DEBOUNCE_MS = 180;

    document.querySelectorAll('.user-typeahead-wrap').forEach(function (wrap) {
        var input = wrap.querySelector('.user-typeahead-input');
        var results = wrap.querySelector('.user-typeahead-results');
        if (!input || !results) return;

        var timer = null;
        var activeIndex = -1;

        function close() {
            results.innerHTML = '';
            results.style.display = 'none';
            activeIndex = -1;
        }

        function open() {
            results.style.display = 'block';
        }

        function render(items) {
            results.innerHTML = '';
            if (!items || !items.length) { close(); return; }
            items.forEach(function (item, idx) {
                var a = document.createElement('button');
                a.type = 'button';
                a.className = 'list-group-item list-group-item-action user-typeahead-item';
                a.dataset.username = item.userName || '';
                var bio = item.isBiographical ? ' <span class="badge bg-light text-muted ms-1">📜 biographical</span>' : '';
                var label = (item.displayName ? '<strong>' + escapeHtml(item.displayName) + '</strong> ' : '')
                          + '<span class="text-muted">@' + escapeHtml(item.userName || '') + '</span>'
                          + (item.email ? '<div class="small text-muted">' + escapeHtml(item.email) + '</div>' : '')
                          + bio;
                a.innerHTML = label;
                a.addEventListener('click', function () {
                    input.value = item.userName || '';
                    close();
                    input.focus();
                });
                results.appendChild(a);
            });
            open();
            activeIndex = -1;
        }

        function escapeHtml(s) {
            return (s || '').replace(/[&<>"']/g, function (c) {
                return ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]);
            });
        }

        function search(q) {
            fetch('/Admin/SearchUsers?q=' + encodeURIComponent(q), { credentials: 'same-origin' })
                .then(function (r) { return r.ok ? r.json() : []; })
                .then(render)
                .catch(close);
        }

        input.addEventListener('input', function () {
            var q = input.value.trim().replace(/^@/, '');
            clearTimeout(timer);
            if (q.length < 2) { close(); return; }
            timer = setTimeout(function () { search(q); }, DEBOUNCE_MS);
        });

        input.addEventListener('keydown', function (e) {
            var items = results.querySelectorAll('.user-typeahead-item');
            if (!items.length) return;
            if (e.key === 'ArrowDown') {
                e.preventDefault();
                activeIndex = Math.min(items.length - 1, activeIndex + 1);
                items.forEach(function (it, i) { it.classList.toggle('active', i === activeIndex); });
            } else if (e.key === 'ArrowUp') {
                e.preventDefault();
                activeIndex = Math.max(0, activeIndex - 1);
                items.forEach(function (it, i) { it.classList.toggle('active', i === activeIndex); });
            } else if (e.key === 'Enter' && activeIndex >= 0) {
                e.preventDefault();
                items[activeIndex].click();
            } else if (e.key === 'Escape') {
                close();
            }
        });

        document.addEventListener('click', function (e) {
            if (!wrap.contains(e.target)) close();
        });
    });
})();

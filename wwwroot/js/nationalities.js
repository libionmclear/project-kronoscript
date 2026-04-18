// Nationality multi-picker.
// Element: <div class="nationality-picker" data-target="#hiddenInput">
//   <input class="nat-search">
//   <div class="nat-results"></div>
//   <div class="nat-bubbles"></div>
// </div>
// The hidden input value is a comma-separated list of ISO 3166-1 alpha-2 codes.
// Renders flag via Unicode regional indicator characters (works in all modern browsers
// except Windows native — which falls back to the country code initials).
(function () {
    var COUNTRIES = [
        {c:"AR",n:"Argentina"},{c:"AU",n:"Australia"},{c:"AT",n:"Austria"},{c:"BE",n:"Belgium"},
        {c:"BR",n:"Brazil"},{c:"CA",n:"Canada"},{c:"CL",n:"Chile"},{c:"CN",n:"China"},
        {c:"CO",n:"Colombia"},{c:"HR",n:"Croatia"},{c:"CZ",n:"Czechia"},{c:"DK",n:"Denmark"},
        {c:"EG",n:"Egypt"},{c:"FI",n:"Finland"},{c:"FR",n:"France"},{c:"DE",n:"Germany"},
        {c:"GR",n:"Greece"},{c:"HU",n:"Hungary"},{c:"IS",n:"Iceland"},{c:"IN",n:"India"},
        {c:"ID",n:"Indonesia"},{c:"IE",n:"Ireland"},{c:"IL",n:"Israel"},{c:"IT",n:"Italy"},
        {c:"JP",n:"Japan"},{c:"KE",n:"Kenya"},{c:"MX",n:"Mexico"},{c:"NL",n:"Netherlands"},
        {c:"NZ",n:"New Zealand"},{c:"NG",n:"Nigeria"},{c:"NO",n:"Norway"},{c:"PK",n:"Pakistan"},
        {c:"PE",n:"Peru"},{c:"PH",n:"Philippines"},{c:"PL",n:"Poland"},{c:"PT",n:"Portugal"},
        {c:"RO",n:"Romania"},{c:"RU",n:"Russia"},{c:"SA",n:"Saudi Arabia"},{c:"SG",n:"Singapore"},
        {c:"ZA",n:"South Africa"},{c:"KR",n:"South Korea"},{c:"ES",n:"Spain"},{c:"SE",n:"Sweden"},
        {c:"CH",n:"Switzerland"},{c:"TW",n:"Taiwan"},{c:"TH",n:"Thailand"},{c:"TR",n:"Türkiye"},
        {c:"UA",n:"Ukraine"},{c:"AE",n:"United Arab Emirates"},{c:"GB",n:"United Kingdom"},
        {c:"US",n:"United States"},{c:"VN",n:"Vietnam"}
    ];
    var byCode = {};
    COUNTRIES.forEach(function (x) { byCode[x.c] = x.n; });

    function flagImg(code, size) {
        if (!code || code.length !== 2) return '';
        var lc = code.toLowerCase();
        var w = size || 20;
        var h = Math.round(w * 0.75);
        return '<img src="https://flagcdn.com/' + w + 'x' + h + '/' + lc + '.png" ' +
               'srcset="https://flagcdn.com/' + (w*2) + 'x' + (h*2) + '/' + lc + '.png 2x" ' +
               'width="' + w + '" height="' + h + '" alt="' + code.toUpperCase() + '" ' +
               'style="border-radius:2px;vertical-align:middle;box-shadow:0 0 0 1px rgba(0,0,0,0.1);" />';
    }

    document.querySelectorAll('.nationality-picker').forEach(function (root) {
        var hiddenSel = root.dataset.target;
        var hidden = document.querySelector(hiddenSel);
        if (!hidden) return;

        var search  = root.querySelector('.nat-search');
        var results = root.querySelector('.nat-results');
        var bubbles = root.querySelector('.nat-bubbles');
        var selected = (hidden.value || '').split(',').map(function (s) { return s.trim().toUpperCase(); }).filter(Boolean);

        function syncHidden() { hidden.value = selected.join(','); }

        function bubble(code) {
            var b = document.createElement('span');
            b.className = 'tag-bubble';
            b.dataset.code = code;
            b.innerHTML = flagImg(code, 18) + '<span style="margin-left:6px;">' + (byCode[code] || code) + '</span>';
            var rm = document.createElement('button');
            rm.type = 'button';
            rm.innerHTML = '&times;';
            rm.addEventListener('click', function () {
                selected = selected.filter(function (x) { return x !== code; });
                b.remove();
                syncHidden();
            });
            b.appendChild(rm);
            return b;
        }

        function add(code) {
            if (!code || selected.indexOf(code) !== -1) return;
            selected.push(code);
            bubbles.appendChild(bubble(code));
            syncHidden();
        }

        // Pre-populate
        selected.forEach(function (c) { bubbles.appendChild(bubble(c)); });

        search.addEventListener('input', function () {
            var q = this.value.trim().toLowerCase();
            results.innerHTML = '';
            if (q.length < 1) { results.style.display = 'none'; return; }
            var matches = COUNTRIES.filter(function (x) {
                return (x.n.toLowerCase().includes(q) || x.c.toLowerCase() === q) && selected.indexOf(x.c) === -1;
            }).slice(0, 12);
            if (!matches.length) { results.style.display = 'none'; return; }
            matches.forEach(function (x) {
                var btn = document.createElement('button');
                btn.type = 'button';
                btn.className = 'list-group-item list-group-item-action py-1 px-2 d-flex align-items-center gap-2';
                btn.innerHTML = flagImg(x.c, 22) +
                                '<span class="small">' + x.n + '</span>' +
                                '<span class="ms-auto text-muted small">' + x.c + '</span>';
                btn.addEventListener('mousedown', function (e) {
                    e.preventDefault();
                    add(x.c);
                    search.value = '';
                    results.style.display = 'none';
                });
                results.appendChild(btn);
            });
            results.style.display = 'block';
        });

        search.addEventListener('blur', function () {
            setTimeout(function () { results.style.display = 'none'; }, 150);
        });
    });
})();

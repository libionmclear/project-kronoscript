// My Story Told - Site JavaScript

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
        var text = document.createElement('div');
        text.textContent = c.body || '';
        body.appendChild(head);
        body.appendChild(text);
        b.appendChild(avatar);
        b.appendChild(body);
        return b;
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
        // Comments only for images (server still allows on videos but bubbles look weird)
        commentForm.style.display = isVideo ? 'none' : '';
        bubblesL.style.display = isVideo ? 'none' : '';
        bubblesR.style.display = isVideo ? 'none' : '';
        loadComments(it.id);
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

    document.querySelectorAll('.toggle-comments-inline').forEach(function (btn) {
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
});

// Feed post expand/collapse
document.addEventListener('DOMContentLoaded', function () {
    document.querySelectorAll('.btn-expand-post').forEach(function (btn) {
        btn.addEventListener('click', function (e) {
            e.preventDefault();
            e.stopPropagation();
            var wrap = btn.closest('.post-expand-wrap');
            if (!wrap) return;
            wrap.classList.toggle('expanded');
        });
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

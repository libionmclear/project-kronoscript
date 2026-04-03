// My Story Told - Site JavaScript

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

    // Apply saved image positions (object-position) from localStorage
    document.querySelectorAll('.post-media-wrap img').forEach(function (el) {
        var saved = el.src ? localStorage.getItem('imgpos:' + el.src) : null;
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

// ── Image viewer modal with pan & save ──────────────────────────────────────
function initImageModal() {
    var modal = null, modalImg = null, modalInner = null;
    var isDragging = false;
    var startX, startY, tx = 0, ty = 0;
    var currentSrc = '';

    function createModal() {
        modal = document.createElement('div');
        modal.style.cssText = 'display:none;position:fixed;inset:0;z-index:9999;background:rgba(0,0,0,0.88);justify-content:center;align-items:center;';

        modalInner = document.createElement('div');
        modalInner.style.cssText = 'position:relative;display:inline-block;cursor:grab;user-select:none;max-width:92vw;max-height:92vh;overflow:hidden;';

        modalImg = document.createElement('img');
        modalImg.style.cssText = 'display:block;max-width:92vw;max-height:92vh;-webkit-user-drag:none;user-select:none;';

        // Close button
        var closeBtn = document.createElement('button');
        closeBtn.innerHTML = '✕';
        closeBtn.style.cssText = 'position:absolute;top:8px;right:8px;background:rgba(0,0,0,0.55);color:#fff;border:none;border-radius:50%;width:30px;height:30px;font-size:15px;cursor:pointer;z-index:2;line-height:1;';
        closeBtn.addEventListener('click', closeModal);

        // Save position button
        var saveBtn = document.createElement('button');
        saveBtn.innerHTML = '💾 Save position';
        saveBtn.style.cssText = 'position:absolute;bottom:10px;right:10px;background:rgba(0,0,0,0.6);color:#fff;border:none;border-radius:6px;padding:5px 12px;font-size:12px;cursor:pointer;z-index:2;';
        saveBtn.addEventListener('click', function () {
            localStorage.setItem('imgpos:' + currentSrc, JSON.stringify({ x: tx, y: ty }));
            // Apply saved position to page thumbnails
            document.querySelectorAll('.post-media-wrap img[src="' + currentSrc + '"]').forEach(function (el) {
                el.style.objectPosition = 'calc(50% + ' + tx + 'px) calc(50% + ' + ty + 'px)';
            });
            saveBtn.innerHTML = '✔ Saved!';
            setTimeout(function () { saveBtn.innerHTML = '💾 Save position'; }, 1500);
        });

        // Hint label
        var hint = document.createElement('div');
        hint.innerHTML = 'Drag to reposition';
        hint.style.cssText = 'position:absolute;bottom:10px;left:10px;background:rgba(0,0,0,0.5);color:rgba(255,255,255,0.75);border-radius:4px;padding:3px 8px;font-size:11px;pointer-events:none;z-index:2;';

        // Drag events
        modalImg.addEventListener('mousedown', function (e) {
            isDragging = true;
            startX = e.clientX - tx;
            startY = e.clientY - ty;
            modalInner.style.cursor = 'grabbing';
            e.preventDefault();
        });
        document.addEventListener('mousemove', function (e) {
            if (!isDragging) return;
            var maxX = Math.max(0, (modalImg.naturalWidth - modalInner.offsetWidth) / 2);
            var maxY = Math.max(0, (modalImg.naturalHeight - modalInner.offsetHeight) / 2);
            tx = Math.max(-maxX, Math.min(maxX, e.clientX - startX));
            ty = Math.max(-maxY, Math.min(maxY, e.clientY - startY));
            modalImg.style.transform = 'translate(' + tx + 'px, ' + ty + 'px)';
        });
        document.addEventListener('mouseup', function () {
            if (isDragging) { isDragging = false; modalInner.style.cursor = 'grab'; }
        });

        // Touch support
        modalImg.addEventListener('touchstart', function (e) {
            if (e.touches.length !== 1) return;
            isDragging = true;
            startX = e.touches[0].clientX - tx;
            startY = e.touches[0].clientY - ty;
            e.preventDefault();
        }, { passive: false });
        modalImg.addEventListener('touchmove', function (e) {
            if (!isDragging || e.touches.length !== 1) return;
            var maxX = Math.max(0, (modalImg.naturalWidth - modalInner.offsetWidth) / 2);
            var maxY = Math.max(0, (modalImg.naturalHeight - modalInner.offsetHeight) / 2);
            tx = Math.max(-maxX, Math.min(maxX, e.touches[0].clientX - startX));
            ty = Math.max(-maxY, Math.min(maxY, e.touches[0].clientY - startY));
            modalImg.style.transform = 'translate(' + tx + 'px, ' + ty + 'px)';
            e.preventDefault();
        }, { passive: false });
        modalImg.addEventListener('touchend', function () { isDragging = false; });

        modalInner.appendChild(modalImg);
        modalInner.appendChild(closeBtn);
        modalInner.appendChild(saveBtn);
        modalInner.appendChild(hint);
        modal.appendChild(modalInner);
        document.body.appendChild(modal);

        // Close on backdrop click
        modal.addEventListener('click', function (e) { if (e.target === modal) closeModal(); });
        document.addEventListener('keydown', function (e) { if (e.key === 'Escape') closeModal(); });
    }

    function openModal(src) {
        if (!modal) createModal();
        currentSrc = src;
        tx = 0; ty = 0;
        var saved = localStorage.getItem('imgpos:' + src);
        if (saved) {
            try { var p = JSON.parse(saved); tx = p.x; ty = p.y; } catch (e) {}
        }
        modalImg.src = src;
        modalImg.style.transform = 'translate(' + tx + 'px, ' + ty + 'px)';
        modal.style.display = 'flex';
        document.body.style.overflow = 'hidden';
    }

    function closeModal() {
        if (modal) modal.style.display = 'none';
        document.body.style.overflow = '';
    }

    // Attach to all post media images
    document.querySelectorAll('.post-media-wrap img').forEach(function (el) {
        el.style.cursor = 'zoom-in';
        el.addEventListener('click', function (e) {
            e.preventDefault();
            e.stopPropagation();
            openModal(el.src);
        });
    });
}

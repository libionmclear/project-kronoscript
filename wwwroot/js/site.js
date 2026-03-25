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
});

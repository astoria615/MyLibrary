(function () {
    if (localStorage.getItem('darkMode') === 'true') {
        document.body.classList.add('dark');
    }
})();

function selectBook(bookId, context) {
    var panel = document.getElementById('detail-panel');

    // Toggle off if same book clicked
    if (panel && panel.dataset.bookId == bookId && panel.style.display !== 'none') {
        panel.style.display = 'none';
        panel.dataset.bookId = '';
        document.querySelectorAll('.book-card.selected, tr.selected').forEach(function (el) {
            el.classList.remove('selected');
        });
        return;
    }

    // Remove previous selection
    document.querySelectorAll('.book-card.selected, tr.selected').forEach(function (el) {
        el.classList.remove('selected');
    });

    // Find and highlight clicked card
    document.querySelectorAll('.book-card').forEach(function (el) {
        if (el.getAttribute('onclick') === 'selectBook(' + bookId + ', \'home\')' ||
            el.getAttribute('onclick') === 'selectBook(' + bookId + ', \'list\')') {
            el.classList.add('selected');
        }
    });
    document.querySelectorAll('tbody tr').forEach(function (el) {
        if (el.getAttribute('onclick') && el.getAttribute('onclick').indexOf('selectBook(' + bookId) !== -1) {
            el.classList.add('selected');
        }
    });

    // Show loading state
    panel.style.display = 'flex';
    panel.innerHTML = '<p style="color:#fff;opacity:.7;font-size:13px;margin:auto">Loading...</p>';

    // Fetch detail
    fetch('/Guest/BookDetail?id=' + bookId)
        .then(function (r) {
            if (!r.ok) throw new Error('Not found');
            return r.text();
        })
        .then(function (html) {
            panel.innerHTML = html;
            panel.dataset.bookId = bookId;
        })
        .catch(function () {
            panel.style.display = 'none';
        });
}

function toggleCategoryMenu(e) {
    e.preventDefault();
    var dd = document.getElementById('cat-dropdown');
    if (dd) dd.style.display = dd.style.display === 'none' ? 'block' : 'none';
}

document.addEventListener('click', function (e) {
    var toggle = document.getElementById('cat-toggle');
    var dd = document.getElementById('cat-dropdown');
    if (dd && toggle && !toggle.contains(e.target) && !dd.contains(e.target)) {
        dd.style.display = 'none';
    }
    var btn = document.getElementById('sort-btn');
    var menu = document.getElementById('sort-menu');
    if (menu && btn && !btn.contains(e.target) && !menu.contains(e.target)) {
        menu.style.display = 'none';
    }
});

function toggleSortMenu() {
    var m = document.getElementById('sort-menu');
    if (m) m.style.display = m.style.display === 'none' ? 'block' : 'none';
}

function toggleDarkMode(on) {
    document.body.classList.toggle('dark', on);
    localStorage.setItem('darkMode', on);
}
// Click outside to close detail panel
document.addEventListener('click', function (e) {
    var panel = document.getElementById('detail-panel');
    if (!panel || panel.style.display === 'none') return;

    // Check if click was on a book card or inside the panel
    var clickedCard = e.target.closest('.book-card, tr[onclick]');
    var clickedPanel = e.target.closest('#detail-panel');

    if (!clickedCard && !clickedPanel) {
        panel.style.display = 'none';
        panel.dataset.bookId = '';
        document.querySelectorAll('.book-card.selected, tr.selected').forEach(function (el) {
            el.classList.remove('selected');
        });
    }
});
// ── NOTIFICATIONS ──
function toggleNotifications() {
    var dd = document.getElementById('notif-dropdown');
    if (!dd) return;
    if (dd.style.display === 'none') {
        dd.style.display = 'block';
        // Load if empty
        if (dd.innerHTML.trim() === '') {
            fetch('/Reader/Notifications')
                .then(function (r) { return r.text(); })
                .then(function (html) {
                    dd.innerHTML = html;
                    var items = dd.querySelectorAll('.notif-item');
                    var badge = document.getElementById('notif-badge');
                    if (badge) badge.style.display = items.length > 0 ? 'flex' : 'none';
                });
        }
    } else {
        dd.style.display = 'none';
    }
}

// Close notifications on outside click
document.addEventListener('click', function (e) {
    var wrap = document.getElementById('notif-wrap');
    var dd = document.getElementById('notif-dropdown');
    if (dd && wrap && !wrap.contains(e.target)) {
        dd.style.display = 'none';
    }
});

// ── BORROW ──
function borrowBook(bookId) {
    fetch('/Reader/Borrow', {
        method: 'POST',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        body: '__RequestVerificationToken=' + encodeURIComponent(
            document.querySelector('input[name=__RequestVerificationToken]') ?
                document.querySelector('input[name=__RequestVerificationToken]').value : ''
        ) + '&bookId=' + bookId
    })
        .then(function (r) { return r.json(); })
        .then(function (data) {
            alert(data.message);
        });
}
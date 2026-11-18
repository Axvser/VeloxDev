// Copies text via a hidden textarea + execCommand so it works from a Blazor Server
// button click (which is not a synchronous browser user gesture for the async Clipboard API).
window.copyWorkflowInfo = function (text) {
    var ta = document.createElement('textarea');
    ta.value = String(text ?? '');
    ta.setAttribute('readonly', '');
    ta.style.position = 'fixed';
    ta.style.opacity = '0';
    ta.style.pointerEvents = 'none';
    document.body.appendChild(ta);
    ta.select();
    ta.setSelectionRange(0, ta.value.length);
    var ok = false;
    try { ok = document.execCommand('copy'); }
    catch (e) { ok = false; }
    document.body.removeChild(ta);
    return ok;
};

// Called after the output pane (or the Agent conversation) renders a new line. Plain
// scrollTop rather than scrollIntoView: the latter also moves the page itself on a
// narrow viewport.
window.scrollElementToEnd = function (id) {
    var el = document.getElementById(id);
    if (el) el.scrollTop = el.scrollHeight;
};

// Title-strip menus close on an outside click, and the Avalonia-style global shortcuts click the
// hidden command targets the workflow page renders. Kept here rather than in a component so the
// listeners exist exactly once per browser document.
(function () {
    function closeWorkbenchMenus() {
        document.querySelectorAll('.wf-topbar details[open]').forEach(function (d) {
            d.removeAttribute('open');
        });
    }

    window.closeWorkbenchMenus = closeWorkbenchMenus;

    document.addEventListener('click', function (e) {
        if (!e.target.closest('.wf-topbar details')) closeWorkbenchMenus();
    });

    document.addEventListener('keydown', function (e) {
        if (!(e.ctrlKey || e.metaKey)) return;

        var key = (e.key || '').toLowerCase();
        var editable = e.target && (e.target.isContentEditable ||
            /^(input|textarea|select)$/i.test(e.target.tagName));

        if (e.shiftKey && key === 'p') {
            e.preventDefault();
            var command = document.querySelector('.wf-command');
            if (command) command.open = true;
            return;
        }

        if (e.shiftKey || editable) return;

        var id = key === 's' ? 'wf-shortcut-save'
            : key === 'o' ? 'wf-shortcut-load'
            : key === 'z' ? 'wf-shortcut-undo'
            : key === 'y' ? 'wf-shortcut-redo'
            : null;
        if (!id) return;

        var button = document.getElementById(id);
        if (button) {
            e.preventDefault();
            button.click();
        }
    });
})();

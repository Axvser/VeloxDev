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

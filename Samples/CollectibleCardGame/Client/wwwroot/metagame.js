// Native modal focus management, including Escape and returning focus to the opener.
const attached = new WeakSet();
const openers = new WeakMap();
export function showDialog(dialog) {
    if (!attached.has(dialog)) {
        attached.add(dialog);
        dialog.addEventListener('close', () => {
            const opener = openers.get(dialog);
            // Blazor removes the closed dialog; restore focus after that render has settled.
            requestAnimationFrame(() => {
                if (opener?.isConnected) opener.focus({ preventScroll: true });
            });
        });
        dialog.addEventListener('keydown', event => {
            if (event.key !== 'Tab') return;
            const stops = [...dialog.querySelectorAll(
                'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex="0"]'
            )].filter(element => element.getClientRects().length > 0);
            const first = stops[0];
            const last = stops.at(-1);
            if (!first) { event.preventDefault(); dialog.focus(); return; }
            if (event.shiftKey && document.activeElement === first) {
                event.preventDefault(); last.focus();
            } else if (!event.shiftKey && document.activeElement === last) {
                event.preventDefault(); first.focus();
            }
        });
        dialog.addEventListener('click', event => {
            if (event.target !== dialog) return;
            const bounds = dialog.getBoundingClientRect();
            if (event.clientX < bounds.left || event.clientX > bounds.right ||
                event.clientY < bounds.top || event.clientY > bounds.bottom) dialog.close();
        });
    }
    if (dialog.isConnected && !dialog.open) {
        openers.set(dialog, document.activeElement);
        dialog.showModal();
    }
}

export function closeDialog(dialog) {
    if (dialog?.open) dialog.close();
}

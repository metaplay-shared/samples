// The browser half of ConnectionOverlay.razor.

// Shows the dialog as a modal: the browser keeps focus inside it and makes the rest of the page inert. The component
// renders it open, so it shows even without this script; this turns it modal. Escape would close it and leave a game
// that cannot reach the server usable, so the dialog reopens.
export function showModal(dialog) {
    if (dialog.matches(':modal')) {
        return;
    }
    dialog.close();
    dialog.addEventListener('cancel', event => event.preventDefault());
    dialog.addEventListener('close', () => {
        if (dialog.isConnected) {
            dialog.showModal();
        }
    });
    dialog.showModal();
}

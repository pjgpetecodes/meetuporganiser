document.addEventListener('click', event => {
    if (!(event.target instanceof Element)) return;

    document.querySelectorAll('details.events-action-dropdown[open]')
        .forEach(menu => {
            if (!menu.contains(event.target)) {
                menu.open = false;
            }
        });
});

document.addEventListener('keydown', event => {
    if (event.key !== 'Escape') return;

    document.querySelectorAll('details.events-action-dropdown[open]').forEach(menu => {
        const restoreFocus = menu.contains(document.activeElement);
        menu.open = false;
        if (restoreFocus) menu.querySelector('summary').focus();
    });
});

document.addEventListener('toggle', event => {
    const menu = event.target;
    if (!(menu instanceof HTMLElement) || !menu.matches('.event-row-action-items[popover]') || event.newState !== 'open') return;

    const trigger = menu.previousElementSibling;
    const triggerBounds = trigger.getBoundingClientRect();
    const menuBounds = menu.getBoundingClientRect();
    const gutter = 8;
    const left = Math.max(gutter, Math.min(triggerBounds.right - menuBounds.width, window.innerWidth - menuBounds.width - gutter));
    const top = triggerBounds.bottom + menuBounds.height + gutter <= window.innerHeight
        ? triggerBounds.bottom + 4
        : Math.max(gutter, triggerBounds.top - menuBounds.height - 4);

    menu.style.left = `${left}px`;
    menu.style.top = `${top}px`;
}, true);

document.addEventListener('click', event => {
    if (!(event.target instanceof Element)) return;

    const action = event.target.closest('.event-row-action-items .dropdown-item');
    if (action) action.closest('[popover]').hidePopover();
});

function closeEventActionPopovers() {
    document.querySelectorAll('.event-row-action-items:popover-open').forEach(menu => menu.hidePopover());
}

document.addEventListener('scroll', closeEventActionPopovers, true);
window.addEventListener('resize', closeEventActionPopovers);

window.meetupOrganiser = {
    copyText: async function (text) {
        try {
            if (navigator.clipboard && window.isSecureContext) {
                await navigator.clipboard.writeText(text);
                return true;
            }
        } catch (e) { /* fall through to legacy path */ }

        const area = document.createElement('textarea');
        area.value = text;
        area.style.position = 'fixed';
        area.style.opacity = '0';
        document.body.appendChild(area);
        area.focus();
        area.select();
        try {
            return document.execCommand('copy');
        } catch (e) {
            return false;
        } finally {
            document.body.removeChild(area);
        }
    },
    selectFocused: function () {
        const el = document.activeElement;
        if (el && el.select) el.select();
    },
    showDialog: function (dialog) {
        if (dialog && !dialog.open) dialog.showModal();
    },
    closeDialog: function (dialog) {
        if (dialog && dialog.open) dialog.close();
    }
};

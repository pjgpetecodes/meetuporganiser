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
    }
};

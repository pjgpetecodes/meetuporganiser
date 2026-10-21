document.addEventListener('click', event => {
    if (!(event.target instanceof Element)) return;

    document.querySelectorAll('details.organizer-account-menu[open]')
        .forEach(menu => {
            if (!menu.contains(event.target) || event.target.closest('.organizer-account-links a')) {
                menu.open = false;
            }
        });

    document.querySelectorAll('details.events-action-dropdown[open]')
        .forEach(menu => {
            if (!menu.contains(event.target)) {
                menu.open = false;
            }
        });
});

document.addEventListener('keydown', event => {
    if (event.key !== 'Escape') return;

    document.querySelectorAll('details.organizer-account-menu[open]').forEach(menu => {
        const restoreFocus = menu.contains(document.activeElement);
        menu.open = false;
        if (restoreFocus) menu.querySelector('summary').focus();
    });

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

const richEditors = new WeakMap();

function richMarkdown(root) {
    const escape = text => text.replace(/\u00a0/g, ' ')
        .replace(/\{\{\s*[A-Za-z][A-Za-z0-9_]{0,63}\s*\}\}|([\\`*_[\]<>#])/g,
            (match, character) => character ? '\\' + character : match)
        .replace(/^(\s*)([-+]|\d+[.)])(?=\s)/gm, '$1\\$2');
    const wrap = (text, marker) => text.trim() ? text.replace(/^(\s*)([\s\S]*?)(\s*)$/, `$1${marker}$2${marker}$3`) : text;
    function read(node) {
        if (node.nodeType === Node.TEXT_NODE) return escape(node.textContent);
        if (node.nodeType !== Node.ELEMENT_NODE) return '';
        const tag = node.tagName;
        if (['SCRIPT', 'STYLE', 'IMG', 'IFRAME', 'OBJECT'].includes(tag)) return '';
        if (tag === 'BR') return '\n';
        if (tag === 'UL' || tag === 'OL') {
            return '\n\n' + [...node.children].filter(child => child.tagName === 'LI')
                .map((item, index) => (tag === 'OL' ? `${index + 1}. ` : '- ') +
                    [...item.childNodes].map(read).join('').trim().replace(/\n/g, '\n    ')).join('\n') + '\n\n';
        }
        const text = [...node.childNodes].map(read).join('');
        if (tag === 'STRONG' || tag === 'B') return wrap(text, '**');
        if (tag === 'EM' || tag === 'I') return wrap(text, '_');
        if (tag === 'A') {
            const href = node.getAttribute('href') ?? '';
            if (/^https?:\/\//i.test(href)) return `[${text}](${href.replace(/[()\\\s]/g,
                char => `%${char.charCodeAt(0).toString(16).toUpperCase()}`)})`;
            return text;
        }
        if (/^H[1-6]$/.test(tag)) return `\n\n${'#'.repeat(Number(tag[1]))} ${text.trim()}\n\n`;
        if (tag === 'P' || tag === 'DIV') return `\n\n${text.trim()}\n\n`;
        return text;
    }
    return [...root.childNodes].map(read).join('').replace(/\n{3,}/g, '\n\n').trim();
}

function richCommand(editor, command, heading = 'h2') {
    const state = richEditors.get(editor);
    if (!state) return;
    editor.focus();
    const selection = window.getSelection();
    if (state.range && editor.contains(state.range.commonAncestorContainer)) {
        selection.removeAllRanges();
        selection.addRange(state.range);
    }
    const commands = { bold: 'bold', italic: 'italic', heading: 'formatBlock', bullets: 'insertUnorderedList',
        numbers: 'insertOrderedList', undo: 'undo', redo: 'redo' };
    if (command === 'heading' && !/^(h[1-6]|p)$/.test(heading)) return;
    let value = command === 'heading' ? heading : null;
    let nativeCommand = commands[command];
    if (command === 'link') {
        value = window.prompt('Link address (http:// or https://)', 'https://');
        if (value === null) return;
        let url;
        try { url = new URL(value); } catch { state.error('Enter a valid http:// or https:// link.'); return; }
        if (!['https:', 'http:'].includes(url.protocol) || url.username || url.password) {
            state.error('Links must use http:// or https:// without credentials.');
            return;
        }
        value = url.href;
        nativeCommand = 'createLink';
    }
    if (!nativeCommand) return;
    if (!document.execCommand(nativeCommand, false, value)) {
        state.error('The browser could not apply this formatting. Select text and try again.');
        return;
    }
    state.changed();
}

window.meetupOrganiser = {
    initRichEditor: function (editor, callback) {
        if (richEditors.has(editor)) return;
        const toolbar = editor.parentElement.querySelector('[role=toolbar]');
        const notice = editor.parentElement.querySelector('.rich-save-error');
        let queue = Promise.resolve();
        const state = {
            range: null,
            changed: () => {
                if (state.composing) return;
                state.remember();
                const markdown = richMarkdown(editor);
                queue = queue.then(async () => {
                    await callback.invokeMethodAsync('RichTextChanged', markdown);
                    editor.removeAttribute('aria-invalid');
                    notice.hidden = true;
                }).catch(error => {
                    console.error('Unable to save rich-text changes', error);
                    editor.setAttribute('aria-invalid', 'true');
                    notice.textContent = 'Changes could not reach the app. Copy your text before reloading or leaving this page.';
                    notice.hidden = false;
                });
            },
            error: message => callback.invokeMethodAsync('RichTextError', message),
            remember: () => {
                const selection = window.getSelection();
                if (selection.rangeCount && editor.contains(selection.getRangeAt(0).commonAncestorContainer))
                    state.range = selection.getRangeAt(0).cloneRange();
            },
            keydown: event => {
                if (!(event.ctrlKey || event.metaKey) || event.altKey) return;
                const key = event.key.toLowerCase();
                const command = event.shiftKey && ['7', '&'].includes(key) ? 'numbers'
                    : event.shiftKey && ['8', '*'].includes(key) ? 'bullets'
                    : !event.shiftKey ? { b: 'bold', i: 'italic', k: 'link' }[key] : null;
                if (!command) return;
                event.preventDefault();
                state.remember();
                richCommand(editor, command);
            },
            paste: event => {
                event.preventDefault();
                if (!document.execCommand('insertText', false, event.clipboardData.getData('text/plain')))
                    state.error('Unable to paste. Try typing the content instead.');
                else state.changed();
            },
            drop: event => { event.preventDefault(); state.error('Drag-and-drop is not supported. Paste text instead.'); },
            pointerdown: event => {
                if (event.target.closest('[data-rich-command]')) { state.remember(); event.preventDefault(); }
                else if (event.target.closest('[data-rich-heading]')) state.remember();
            },
            change: event => {
                if (event.target.matches('[data-rich-heading]')) {
                    richCommand(editor, 'heading', event.target.value);
                    event.target.value = '';
                }
            },
            click: event => {
                const button = event.target.closest('[data-rich-command]');
                if (button) richCommand(editor, button.dataset.richCommand);
            },
            compositionstart: () => { state.composing = true; },
            compositionend: () => { state.composing = false; state.changed(); }
        };
        richEditors.set(editor, state);
        const listeners = [[editor, 'input', state.changed], [editor, 'keydown', state.keydown],
            [editor, 'paste', state.paste], [editor, 'drop', state.drop],
            [editor, 'keyup', state.remember], [editor, 'mouseup', state.remember],
            [editor, 'compositionstart', state.compositionstart], [editor, 'compositionend', state.compositionend],
            [toolbar, 'pointerdown', state.pointerdown], [toolbar, 'click', state.click],
            [toolbar, 'change', state.change]];
        for (const [target, name, handler] of listeners) target.addEventListener(name, handler);
        state.dispose = () => { for (const [target, name, handler] of listeners) target.removeEventListener(name, handler); };
    },
    setRichEditor: function (editor, html) {
        editor.innerHTML = html;
        const state = richEditors.get(editor);
        if (state) state.range = null;
    },
    disposeRichEditor: function (editor) {
        richEditors.get(editor)?.dispose();
        richEditors.delete(editor);
    },
    copyMarkdown: async function (text, html) {
        if (navigator.clipboard?.write && typeof ClipboardItem === 'function' && window.isSecureContext) {
            await navigator.clipboard.write([new ClipboardItem({
                'text/html': new Blob([html], { type: 'text/html' }),
                'text/plain': new Blob([text], { type: 'text/plain' })
            })]);
            return 'html';
        }
        if (!await window.meetupOrganiser.copyText(text)) throw new Error('Clipboard access failed.');
        return 'text';
    },
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

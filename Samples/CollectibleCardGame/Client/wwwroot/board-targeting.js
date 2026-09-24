/* Pointer samples stay in the browser: targeting never waits for a .NET or server round trip. */
window.stickyPawsTargeting = (() => {
    const controllers = new WeakMap();
    const sourceSelector = '.handcard[data-selected="true"], .critter[data-selected="true"]';
    const targetSelector = '.critter[data-targetable="true"], .den[data-targetable="true"]';

    function create(svg, receiver) {
        const board = svg.closest('.board-frame');
        const abort = new AbortController();
        const options = { signal: abort.signal, passive: true };
        let kind = 'None';
        let frame = 0;
        let pointer = null;
        let keyboard = false;
        let aimedTarget = null;
        let gesture = null;
        let settling = false;
        let suppressClickUntil = 0;
        const paths = [...svg.querySelectorAll('.targeting-arrow-shadow, .targeting-arrow-body, .targeting-arrow-thread')];
        const head = svg.querySelector('.targeting-arrow-head');
        const origin = svg.querySelector('circle');
        const counter = svg.querySelector('.targeting-counter');

        function showCounter(target, sourceRect, bounds) {
            if (!counter) return;
            const preview = kind === 'Attack' ? target?.querySelector('.delta-preview[data-counter-amount]') : null;
            if (!preview) { counter.setAttribute('hidden', ''); return; }
            const amount = Number(preview.dataset.counterAmount);
            if (!Number.isInteger(amount) || amount < 0) { counter.setAttribute('hidden', ''); return; }
            const blocked = preview.dataset.counterBlocked === 'true';
            const lethal = preview.dataset.counterLethal === 'true';
            const value = amount > 0 ? `−${amount}` : '0';
            const marked = blocked || lethal;
            const width = Math.max(44, value.length * 12 + (marked ? 24 : 12));
            const position = point(sourceRect, bounds);
            position.y = (sourceRect.top + sourceRect.height * .12 - bounds.top) * board.clientHeight / bounds.height;
            const scale = Math.max(.75, Math.min(1.6, sourceRect.height * board.clientHeight / bounds.height / 140));
            counter.setAttribute('transform', `translate(${position.x} ${position.y}) scale(${scale})`);
            counter.dataset.amount = String(amount);
            counter.dataset.blocked = String(blocked);
            counter.dataset.lethal = String(lethal);
            const rect = counter.querySelector('rect');
            rect.setAttribute('x', -width / 2);
            rect.setAttribute('width', width);
            const text = counter.querySelector('text');
            if (text.textContent !== value) text.textContent = value;
            text.setAttribute('x', marked ? -8 : 0);
            counter.querySelector('.targeting-counter-mark').setAttribute('transform', `translate(${width / 2 - 12} 0)`);
            counter.removeAttribute('hidden');
        }

        function aim(target) {
            if (aimedTarget !== target) aimedTarget?.classList.remove('is-aimed-target');
            aimedTarget = target;
            aimedTarget?.classList.add('is-aimed-target');
        }
        function hide() { svg.setAttribute('hidden', ''); aim(null); }
        function point(rect, bounds) {
            return { x: (rect.left + rect.width / 2 - bounds.left) * board.clientWidth / bounds.width,
                y: (rect.top + rect.height / 2 - bounds.top) * board.clientHeight / bounds.height };
        }
        function draw() {
            frame = 0;
            if (kind === 'None' || !svg.isConnected) { hide(); return; }
            frame = requestAnimationFrame(draw);
            const source = board.querySelector(sourceSelector);
            const bounds = board.getBoundingClientRect();
            if (!source || !bounds.width || !bounds.height || document.hidden) { hide(); return; }
            let target = null;
            if (keyboard) target = document.activeElement?.closest(targetSelector);
            else if (pointer) target = document.elementFromPoint(pointer.x, pointer.y)?.closest(targetSelector);
            if (target && !board.contains(target)) target = null;
            aim(target);
            const sourceRect = source.getBoundingClientRect();
            const start = point(sourceRect, bounds);
            // Before pointer input (keyboard/touch selection), a short stem makes the selected source explicit.
            const end = target ? point(target.getBoundingClientRect(), bounds)
                : pointer && !keyboard ? { x: (pointer.x - bounds.left) * board.clientWidth / bounds.width,
                    y: (pointer.y - bounds.top) * board.clientHeight / bounds.height }
                : { x: start.x, y: Math.max(24, start.y - 100) };
            end.x = Math.max(14, Math.min(board.clientWidth - 14, end.x));
            end.y = Math.max(14, Math.min(board.clientHeight - 14, end.y));
            const dx = end.x - start.x, dy = end.y - start.y;
            const length = Math.hypot(dx, dy);
            if (length < 28) { hide(); return; }
            const bend = Math.min(85, length * .22);
            const control = { x: (start.x + end.x) / 2 + dy / length * bend,
                y: (start.y + end.y) / 2 - dx / length * bend };
            const angle = Math.atan2(end.y - control.y, end.x - control.x);
            const ux = Math.cos(angle), uy = Math.sin(angle);
            const tipBase = { x: end.x - ux * 14, y: end.y - uy * 14 };
            const curve = `M ${start.x} ${start.y} Q ${control.x} ${control.y} ${tipBase.x} ${tipBase.y}`;
            for (const path of paths) path.setAttribute('d', curve);
            head.setAttribute('d', `M ${end.x} ${end.y} L ${tipBase.x - uy * 8} ${tipBase.y + ux * 8} L ${tipBase.x + uy * 8} ${tipBase.y - ux * 8} Z`);
            origin.setAttribute('cx', start.x);
            origin.setAttribute('cy', start.y);
            svg.setAttribute('viewBox', `0 0 ${board.clientWidth} ${board.clientHeight}`);
            svg.dataset.snapped = String(Boolean(target));
            showCounter(target, sourceRect, bounds);
            svg.removeAttribute('hidden');
        }
        // Keep movement entirely in JS. Only beginning an aim and releasing it cross into Blazor.
        // Track native touch contacts, without transferring pointer capture from a card during a render.
        const activeOptions = { signal: abort.signal, passive: false, capture: true };
        const nextFrame = () => new Promise(resolve => requestAnimationFrame(resolve));
        async function finishDrag(cancelled, x, y) {
            const current = gesture;
            if (!current) return;
            gesture = null;

            if (!current.started) return;
            settling = true;
            suppressClickUntil = performance.now() + 750;
            try {
                const accepted = await current.ready;
                // Let the selection render and publish the legal target attributes, even on a quick flick.
                await nextFrame();
                await nextFrame();
                const hit = document.elementFromPoint(x, y);
                const playZone = board.querySelector('.touch-play-zone')?.getBoundingClientRect();
                const target = hit?.closest(targetSelector);
                let targetKind = '', targetId = -1;
                if (!cancelled && target && board.contains(target)) {
                    targetKind = target.classList.contains('den') ? 'den' : 'critter';
                    targetId = Number(targetKind === 'den' ? target.dataset.seat : target.dataset.instance);
                } else if (!cancelled && current.fromHand && hit && board.contains(hit)
                           && !hit.closest('.hand, button, [role="dialog"]')
                           && playZone && x >= playZone.left && x <= playZone.right
                           && y >= playZone.top && y <= playZone.bottom) {
                    targetKind = 'board';
                }
                if (accepted) await receiver.invokeMethodAsync('EndDrag', targetKind, targetId);
            } catch { /* A disconnected/disposed board cannot submit a gesture. */ }
            finally {
                board.classList.remove('is-touch-dragging');
                current.source.classList.remove('is-drag-source');
                pointer = null;
                keyboard = true;
                aim(null);
                settling = false;
            }
        }
        function startTouch(event) {
            if (event.touches.length !== 1) { finishDrag(true, 0, 0); return; }
            if (settling || !receiver) return;
            suppressClickUntil = 0;
            const touch = event.changedTouches[0];
            const source = event.target.closest('.handcard[data-playable="true"], .critter[data-selectable="true"]');
            if (!source || !board.contains(source) || source.closest('.hand-mulligan')) return;
            gesture = { id: touch.identifier, x: touch.clientX, y: touch.clientY, source,
                fromHand: source.classList.contains('handcard'), started: false };
        }
        function moveTouch(event) {
            if (!gesture) return;
            const touch = [...event.changedTouches].find(t => t.identifier === gesture.id);
            if (!touch) return;
            // Native Touch Events keep the original contact through DOM/focus changes on iOS Safari.
            // Prevent browser panning from taking ownership even before the drag threshold is crossed.
            if (event.cancelable) event.preventDefault();
            pointer = { x: touch.clientX, y: touch.clientY };
            keyboard = false;
            if (!gesture.started && Math.hypot(touch.clientX - gesture.x, touch.clientY - gesture.y) >= 10) {
                gesture.started = true;
                board.classList.add('is-touch-dragging');
                gesture.source.classList.add('is-drag-source');
                gesture.ready = receiver.invokeMethodAsync('BeginDrag', gesture.fromHand, Number(gesture.source.dataset.instance))
                    .catch(error => { console.error('Could not start card drag', error); return false; });
            }
        }
        window.addEventListener('touchstart', startTouch, activeOptions);
        window.addEventListener('touchmove', moveTouch, activeOptions);
        window.addEventListener('touchend', event => {
            if (!gesture) return;
            const touch = [...event.changedTouches].find(t => t.identifier === gesture.id);
            if (!touch) return;
            if (gesture.started && event.cancelable) event.preventDefault();
            finishDrag(false, touch.clientX, touch.clientY);
        }, activeOptions);
        window.addEventListener('touchcancel', () => finishDrag(true, 0, 0), activeOptions);
        // Pointer events are only for mouse/pen aiming. They never own a finger's gesture or its release.
        window.addEventListener('pointermove', event => {
            if (event.pointerType === 'touch') return;
            pointer = { x: event.clientX, y: event.clientY };
            keyboard = false;
        }, options);
        window.addEventListener('pointerdown', event => {
            pointer = { x: event.clientX, y: event.clientY };
            keyboard = event.pointerType === 'touch';
            if (!settling) suppressClickUntil = 0;
        }, options);
        window.addEventListener('click', event => {
            if (event.detail !== 0 && board.contains(event.target) && (settling || performance.now() < suppressClickUntil)) {
                event.preventDefault();
                event.stopImmediatePropagation();
            }
        }, activeOptions);
        window.addEventListener('keydown', event => {
            keyboard = true;
            if (event.key === 'Escape' && kind !== 'None' && receiver) {
                kind = 'None';
                cancelAnimationFrame(frame);
                frame = 0;
                hide();
                receiver.invokeMethodAsync('CancelFromKeyboard').catch(() => {});
            }
        }, options);
        window.addEventListener('blur', () => { finishDrag(true, 0, 0); pointer = null; keyboard = true; }, options);
        document.addEventListener('visibilitychange', () => { if (document.hidden) finishDrag(true, 0, 0); }, options);
        return {
            update(nextKind) {
                kind = nextKind;
                board.classList.toggle('is-targeting', kind !== 'None');
                if (kind === 'None') { cancelAnimationFrame(frame); frame = 0; hide(); }
                else if (!frame) draw();
            },
            dispose() { finishDrag(true, 0, 0); abort.abort(); cancelAnimationFrame(frame); hide();
                board.classList.remove('is-targeting', 'is-touch-dragging'); }
        };
    }
    return {
        update(svg, kind, receiver) {
            let controller = controllers.get(svg);
            if (!controller) { controller = create(svg, receiver); controllers.set(svg, controller); }
            controller.update(kind);
        },
        dispose(svg) {
            controllers.get(svg)?.dispose();
            controllers.delete(svg);
        }
    };
})();

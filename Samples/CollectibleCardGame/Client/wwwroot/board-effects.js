(function () {
    const active = new Set();
    let lastSequence = -1;
    const reduced = window.matchMedia('(prefers-reduced-motion: reduce)');

    function dispose(effect) {
        clearTimeout(effect.timer);
        for (const animation of effect.animations) animation.cancel();
        effect.root.remove();
        active.delete(effect);
    }

    function reset() {
        for (const effect of active) dispose(effect);
        lastSequence = -1;
    }

    function play(sequence, impacts) {
        if (sequence === lastSequence) return;
        lastSequence = sequence;
        const board = document.querySelector('[data-testid="match-board"]');
        if (!board || document.hidden) return;
        for (const impact of impacts) {
            const selector = impact.targetKind === 'den'
                ? `.den[data-seat="${impact.target}"]`
                : `[data-testid="board-critter"][data-instance="${impact.target}"]`;
            const target = board.querySelector(selector);
            if (!target) continue;
            // Each resolution keeps its own owner and finish time, independent of the next server event.
            while (active.size >= 16) dispose(active.values().next().value);
            const moving = impact.targetKind === 'critter' && target.classList.contains('board-motion-source-hidden')
                ? document.querySelector(`[data-testid="board-motion-attacker"][data-instance="${impact.target}"]`)
                : null;
            const rect = (moving ?? target).getBoundingClientRect();
            const preview = target.querySelector('.delta-preview');
            const anchor = moving ? null : preview?.getBoundingClientRect();
            const x = anchor?.width ? anchor.left + anchor.width / 2 : rect.left + rect.width / 2;
            const y = anchor?.height ? anchor.top + anchor.height / 2 : rect.top + rect.height * .12;
            const size = Math.max(24, Math.min(52, rect.height * .22));
            const root = document.createElement('div');
            root.className = `board-impact board-impact-${impact.kind}`;
            root.dataset.testid = 'board-impact';
            root.dataset.kind = impact.kind;
            root.dataset.targetKind = impact.targetKind;
            root.dataset.target = impact.target;
            root.dataset.amount = impact.amount;
            root.setAttribute('aria-hidden', 'true');
            root.style.cssText = `left:${x}px;top:${y}px;--impact-size:${size}px`;
            const bubble = document.createElement('span');
            bubble.className = 'board-impact-value';
            bubble.textContent = impact.kind === 'blocked' ? 'Blocked' : `${impact.kind === 'heal' ? '+' : '−'}${impact.amount}`;
            root.append(bubble);
            document.body.append(root);
            const effect = { root, board, animations: [], timer: null };
            active.add(effect);
            if (!reduced.matches) {
                if (impact.kind === 'damage') {
                    const pow = document.createElement('span');
                    pow.className = 'board-impact-pow';
                    pow.dataset.testid = 'board-impact-pow';
                    pow.textContent = 'POW!';
                    pow.style.top = `${rect.top + rect.height / 2 - y}px`;
                    pow.style.setProperty('--pow-size', `${Math.max(72, Math.min(130, rect.height * .85))}px`);
                    root.append(pow);
                    effect.animations.push(pow.animate([
                        { transform: 'translate(-50%, -50%) rotate(-16deg) scale(.35)', opacity: 0 },
                        { transform: 'translate(-50%, -50%) rotate(-7deg) scale(1.12)', opacity: 1, offset: .18 },
                        { transform: 'translate(-50%, -50%) rotate(-7deg) scale(1)', opacity: 1, offset: .58 },
                        { transform: 'translate(-50%, -50%) rotate(-3deg) scale(1.25)', opacity: 0 },
                    ], { duration: 540, easing: 'ease-out', fill: 'both' }));
                }
                effect.animations.push(bubble.animate([
                    { transform: 'translate(-50%, -50%) scale(.72)', opacity: 0 },
                    { transform: 'translate(-50%, -50%) scale(1.16)', opacity: 1, offset: .15 },
                    { transform: 'translate(-50%, -65%) scale(1)', opacity: 1, offset: .4 },
                    { transform: 'translate(-50%, -110%) scale(.92)', opacity: 1, offset: .76 },
                    { transform: 'translate(-50%, -150%) scale(.85)', opacity: 0 },
                ], { duration: 900, easing: 'cubic-bezier(.2,.7,.25,1)', fill: 'both' }));
                const ring = document.createElement('i');
                ring.className = 'board-impact-ring';
                root.append(ring);
                effect.animations.push(ring.animate([
                    { transform: 'translate(-50%, -50%) scale(.35)', opacity: .9 },
                    { transform: 'translate(-50%, -50%) scale(2.4)', opacity: 0 },
                ], { duration: 480, easing: 'ease-out', fill: 'both' }));
                for (let index = 0; index < 9; index++) {
                    const particle = document.createElement('i');
                    particle.className = 'board-impact-particle';
                    root.append(particle);
                    const angle = (index / 9) * Math.PI * 2 + sequence * .43;
                    const distance = size * (1.05 + (index % 3) * .28);
                    const dx = Math.cos(angle) * distance;
                    const dy = Math.sin(angle) * distance - (impact.kind === 'heal' ? size * .7 : 0);
                    effect.animations.push(particle.animate([
                        { transform: 'translate(-50%, -50%) scale(.2)', opacity: 0 },
                        { opacity: 1, offset: .12 },
                        { opacity: 1, offset: .5 },
                        { transform: `translate(calc(-50% + ${dx}px), calc(-50% + ${dy}px)) rotate(${index * 47}deg) scale(.1)`, opacity: 0 },
                    ], { duration: 620 + (index % 3) * 80, easing: 'ease-out', fill: 'both' }));
                }
            }
            // Reduced motion retains the confirmed value without movement, particles, or flashes.
            effect.timer = setTimeout(() => dispose(effect), reduced.matches ? 1000 : 950);
        }
    }

    new MutationObserver(() => {
        for (const effect of active) if (!effect.board.isConnected) dispose(effect);
    }).observe(document.body, { childList: true, subtree: true });
    document.addEventListener('visibilitychange', () => { if (document.hidden) reset(); });
    window.addEventListener('pagehide', reset);
    window.addEventListener('resize', reset);
    reduced.addEventListener('change', reset);
    window.stickyPawsBoardEffects = { play, reset };
}());

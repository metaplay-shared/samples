(function () {
    let activeAttack = null;
    const attacks = new Set();
    let attackGeneration = 0;
    const activePlays = new Set();
    const retiredHands = new Set();
    const retiredOpponentBacks = new Set();

    // Inspection remains readable at the edge of the table without moving its owner or stealing input.
    function fitTooltips() {
        const board = document.querySelector('[data-testid="match-board"]');
        if (!board) return;
        const bounds = board.getBoundingClientRect();
        for (const tip of board.querySelectorAll('.card-tooltip')) {
            if (getComputedStyle(tip).visibility === 'hidden' || !tip.getClientRects().length) continue;
            tip.style.translate = '0px 0px';
            const rect = tip.getBoundingClientRect();
            const scale = rect.width / tip.offsetWidth || 1;
            const dx = Math.max(bounds.left + 8 - rect.left, Math.min(0, bounds.right - 8 - rect.right));
            const dy = Math.max(bounds.top + 8 - rect.top, Math.min(0, bounds.bottom - 8 - rect.bottom));
            tip.style.translate = `${dx / scale}px ${dy / scale}px`;
        }
    }
    document.addEventListener('pointerover', () => setTimeout(fitTooltips, 550));
    document.addEventListener('focusin', () => setTimeout(fitTooltips, 550));
    window.addEventListener('resize', fitTooltips);

    function hide(source) {
        if (source && !source.classList.contains("board-motion-source-hidden"))
            source.classList.add("board-motion-source-hidden");
    }

    function critter(instance) {
        return document.querySelector(`[data-testid="board-critter"][data-instance="${instance}"]`);
    }

    function den(seat) {
        return document.querySelector(`[data-testid^="den-"][data-seat="${seat}"]`);
    }

    function targetElement(kind, value) {
        if (kind === "critter") return critter(value);
        if (kind === "den") return den(value);
        return null;
    }

    function cloneForMotion(source, testId, size) {
        const clone = source.tagName === "IMG" ? document.createElement("div") : source.cloneNode(true);
        const rect = source.getBoundingClientRect();
        const matrix = new DOMMatrixReadOnly(getComputedStyle(source).transform);
        const width = size?.width ?? source.offsetWidth;
        const height = size?.height ?? source.offsetHeight;
        const origin = { left: rect.left + (rect.width - width) / 2, top: rect.top + (rect.height - height) / 2,
            width, height };
        const scaleX = source.offsetWidth / width;
        const scaleY = source.offsetHeight / height;
        const startTransform = `matrix(${matrix.a * scaleX}, ${matrix.b * scaleX}, ${matrix.c * scaleY}, ${matrix.d * scaleY}, 0, 0)`;
        if (source.tagName === "IMG") {
            const back = source.cloneNode(true);
            back.className = "board-motion-back";
            back.style.cssText = "position:absolute;inset:0;width:100%;height:100%;margin:0;object-fit:contain";
            back.style.opacity = getComputedStyle(source).opacity;
            back.style.objectFit = getComputedStyle(source).objectFit;
            clone.appendChild(back);
        }
        clone.classList.remove("handcard-resolving-play", "handcard-peeking", "critter-beat-attacker", "board-motion-source-hidden");
        clone.classList.add("board-motion-card");
        clone.setAttribute("data-testid", testId);
        clone.setAttribute("aria-hidden", "true");
        clone.inert = true;
        clone.removeAttribute("data-selected");
        clone.removeAttribute("data-marked");
        clone.querySelectorAll(".handcard-peek, .critter-peek, .critter-hover-tag").forEach(node => node.remove());
        clone.style.left = `${origin.left}px`;
        clone.style.top = `${origin.top}px`;
        clone.style.width = `${width}px`;
        clone.style.height = `${height}px`;
        clone.style.setProperty("--card-w", `${width}px`);
        clone.style.transform = startTransform;
        document.body.appendChild(clone);
        hide(source);
        return { clone, source, rect: origin, startTransform };
    }

    function clearPlay(expected) {
        for (const moving of activePlays) {
            if (expected && moving !== expected) continue;
            moving.clone.remove();
            for (const destination of moving.destinations ?? [])
                destination.classList.remove("board-motion-source-hidden");
            if (!retiredHands.has(moving.source.dataset.instance) && !retiredOpponentBacks.has(moving.source))
                moving.source.classList.remove("board-motion-source-hidden");
            activePlays.delete(moving);
        }
    }

    // A later update or viewport resize can reflow and resize a socket while a flight is finishing.
    // Match its complete rendered bounds before handing ownership back to the settled card.
    function alignForHandoff(moving, destination, retry) {
        if (moving.aligning) return false;
        const current = moving.clone.getBoundingClientRect();
        const target = destination.getBoundingClientRect();
        const dx = target.left + target.width / 2 - current.left - current.width / 2;
        const dy = target.top + target.height / 2 - current.top - current.height / 2;
        if (!current.width || !current.height || !target.width || !target.height) return true;
        if (Math.abs(dx) < 1 && Math.abs(dy) < 1
            && Math.abs(target.width - current.width) < 1 && Math.abs(target.height - current.height) < 1)
            return true;
        const from = getComputedStyle(moving.clone).transform;
        const matrix = new DOMMatrixReadOnly(from);
        const scaleX = target.width / current.width;
        const scaleY = target.height / current.height;
        // Scaling the matrix rows scales the viewport bounds, retaining any current rotation/skew.
        // The clone's centred transform origin keeps translation independent of its new size.
        const to = `matrix(${matrix.a * scaleX}, ${matrix.b * scaleY}, ${matrix.c * scaleX}, ${matrix.d * scaleY}, ${matrix.e + dx}, ${matrix.f + dy})`;
        moving.aligning = true;
        const animation = moving.clone.animate([{ transform: from }, { transform: to }],
            { duration: 120, fill: 'forwards', easing: 'ease-out' });
        animation.id = 'sticky-paws-handoff-align';
        animation.finished.then(() => {
            moving.aligning = false;
            if (moving.clone.isConnected) retry();
        }).catch(() => {});
        return false;
    }

    // Both faces share one moving card silhouette. The confirmed board face is blended in while airborne,
    // including weather stats, keywords and sleep, so arrival cannot replace it with a different card.
    function morphToBoard(moving, destination, duration) {
        const from = moving.clone.querySelector(".card-face, .board-motion-back");
        const targetFace = destination?.querySelector(".card-face");
        if (!from || !targetFace || moving.morphed) return;
        moving.morphed = true;
        const to = targetFace.cloneNode(true);
        const sourceFace = moving.source.querySelector(".card-face") ?? moving.source;
        const fromOpacity = getComputedStyle(sourceFace).opacity;
        const toOpacity = getComputedStyle(targetFace).opacity;
        to.style.position = "absolute";
        to.style.inset = "0";
        to.style.width = "100%";
        to.style.height = "100%";
        to.style.margin = "0";
        to.style.pointerEvents = "none";
        const targetCrest = targetFace.querySelector(".card-face-crest");
        const toCrest = to.querySelector(".card-face-crest");
        if (targetCrest && toCrest) {
            const crest = getComputedStyle(targetCrest);
            toCrest.style.left = crest.left;
            toCrest.style.top = crest.top;
            toCrest.style.width = crest.width;
        }
        moving.clone.appendChild(to);
        const timing = { duration: Math.max(120, duration * 0.75), easing: "ease-in-out", fill: "forwards" };
        from.animate([{ opacity: fromOpacity }, { opacity: fromOpacity, offset: 0.25 }, { opacity: 0 }], timing);
        to.animate([{ opacity: 0 }, { opacity: 0, offset: 0.25 }, { opacity: toOpacity }], timing).finished.then(() => {
            moving.morphFinished = true;
            reconcile();
        }).catch(() => {});
    }

    function clearAttack(expected) {
        if (!expected) attackGeneration++;
        for (const moving of attacks) {
            if (expected && moving !== expected) continue;
            moving.clone.remove();
            attacks.delete(moving);
            if (![...attacks].some(other => other.sourceInstance === moving.sourceInstance))
                moving.source.classList.remove("board-motion-source-hidden");
            if (activeAttack === moving) activeAttack = null;
            moving.resolveDone?.();
        }
    }

    function playCard(sourceInstance, targetKind, targetValue, duration, playedByOpponent) {
        let source = document.querySelector(`[data-testid="hand-card"][data-instance="${sourceInstance}"]`);
        const reveal = document.querySelector(`[data-testid="trick-flight-destination"][data-instance="${sourceInstance}"]`);
        const destination = document.querySelector(
            `[data-testid="critter-entry-slot"][data-entry-instance="${sourceInstance}"]`)
            || reveal || targetElement(targetKind, targetValue);
        const opponent = playedByOpponent ?? (!source && destination?.closest(".critter-row-enemy"));
        if (opponent)
            source = [...document.querySelectorAll('[data-testid="opponent-hand"] .enemy-card:not(.board-motion-source-hidden)')].at(-1);
        if (!source || !destination || duration <= 0) return;

        // A slow render can overlap the end of the previous flight with the next published play. Each
        // flight keeps its own visual owner until its own destination has settled.
        const moving = cloneForMotion(source, "board-motion-card", opponent || reveal ? destination.getBoundingClientRect() : null);
        activePlays.add(moving);
        moving.sourceInstance = String(sourceInstance);
        moving.isCritter = destination.dataset.testid === "critter-entry-slot";
        moving.destinations = new Set();
        moving.duration = duration;
        moving.opponent = !!opponent;
        moving.isTrick = !!reveal;
        if (reveal && opponent)
            retiredOpponentBacks.add(source);
        if (reveal)
            morphToBoard(moving, reveal, Math.min(240, duration * 0.4));
        const sourceCrest = source.querySelector(".card-face-crest");
        const movingCrest = moving.clone.querySelector(".card-face-crest");
        if (sourceCrest && movingCrest) {
            const crest = getComputedStyle(sourceCrest);
            movingCrest.animate([
                { left: crest.left, top: crest.top, width: crest.width },
                { left: "78.1%", top: "-2.1%", width: "25%" },
            ], { duration: Math.min(220, duration * 0.5), fill: "forwards", easing: "ease-out" });
        }
        if (moving.isCritter) {
            const arriving = critter(sourceInstance);
            if (arriving) {
                morphToBoard(moving, arriving, duration);
                moving.destinations.add(arriving);
                hide(arriving);
            }
        }
        if (!moving.opponent)
            retiredHands.add(moving.sourceInstance);
        const end = destination.getBoundingClientRect();
        const deltaX = end.left - moving.rect.left + (end.width - moving.rect.width) / 2;
        const deltaY = end.top - moving.rect.top + (end.height - moving.rect.height) / 2;
        const middleRotation = deltaX < 0 ? -5 : 5;
        const frames = [
            { transform: moving.startTransform, filter: "brightness(1)" },
            {
                offset: 0.28,
                transform: `translate(${deltaX * 0.25}px, ${deltaY * 0.2 - 28}px) rotate(${middleRotation}deg) scale(1.18)`,
                filter: "brightness(1.12)",
            },
            {
                offset: 0.76,
                transform: `translate(${deltaX * 0.82}px, ${deltaY * 0.78 - 18}px) rotate(${middleRotation * 0.35}deg) scale(1.08)`,
                filter: "brightness(1.18)",
            },
            { transform: `translate(${deltaX}px, ${deltaY}px) rotate(0deg) scale(1)`, filter: "brightness(1)" },
        ];
        if (reveal) {
            const aimed = targetElement(targetKind, targetValue)?.getBoundingClientRect();
            const finishX = aimed ? aimed.left + aimed.width / 2 - moving.rect.left - moving.rect.width / 2 : deltaX;
            const finishY = aimed ? aimed.top + aimed.height / 2 - moving.rect.top - moving.rect.height / 2 : deltaY;
            frames.splice(0, frames.length,
                { offset: 0, transform: moving.startTransform, opacity: 1 },
                { offset: 0.3, transform: `translate(${deltaX}px, ${deltaY}px) scale(1)`, opacity: 1 },
                { offset: 0.75, transform: `translate(${deltaX}px, ${deltaY}px) scale(1)`, opacity: 1 },
                { offset: 1, transform: `translate(${finishX}px, ${finishY}px) scale(.65)`, opacity: 0 });
        }
        const animation = moving.clone.animate(frames, {
            duration,
            easing: reveal ? "linear" : "cubic-bezier(.2,.72,.22,1)",
            fill: "forwards",
        });
        animation.id = "sticky-paws-card-play";
        animation.finished.then(() => {
            if (!activePlays.has(moving)) return;
            moving.landed = true;
            if (moving.isTrick) {
                clearPlay(moving);
            } else if (!moving.isCritter) {
                moving.clone.animate([{ opacity: 1, scale: "1" }, { opacity: 0, scale: "0.88" }],
                    { duration: 180, fill: "forwards", easing: "ease-out" }).finished
                    .then(() => clearPlay(moving));
            } else {
                reconcile();
            }
        }).catch(() => clearPlay(moving));
    }

    function attack(sourceInstance, targetKind, targetValue, duration) {
        // A returning attacker retains its owner even if another attack has already been published.
        const previous = [...attacks].find(moving => moving.sourceInstance === String(sourceInstance));
        if (previous) {
            const generation = attackGeneration;
            previous.done.then(() => {
                if (generation === attackGeneration)
                    attack(sourceInstance, targetKind, targetValue, duration);
            });
            return;
        }
        const source = critter(sourceInstance);
        const target = targetElement(targetKind, targetValue);
        if (!source || !target || duration <= 0) return;

        activeAttack = cloneForMotion(source, "board-motion-attacker");
        attacks.add(activeAttack);
        activeAttack.done = new Promise(resolve => activeAttack.resolveDone = resolve);
        const targetRect = target.getBoundingClientRect();
        const sourceX = activeAttack.rect.left + activeAttack.rect.width / 2;
        const sourceY = activeAttack.rect.top + activeAttack.rect.height / 2;
        const targetX = targetRect.left + targetRect.width / 2;
        const targetY = targetRect.top + targetRect.height / 2;
        const deltaX = (targetX - sourceX) * 0.72;
        const deltaY = (targetY - sourceY) * 0.72;
        const distance = Math.hypot(deltaX, deltaY) || 1;
        const windupX = -deltaX / distance * 8;
        const windupY = -deltaY / distance * 8;
        const rotation = Math.max(-7, Math.min(7, deltaX / 35));
        activeAttack.heldTransform = `translate(${deltaX}px, ${deltaY}px) rotate(${rotation}deg) scale(1.1)`;
        const animation = activeAttack.clone.animate([
            { transform: activeAttack.startTransform, filter: "brightness(1)" },
            {
                offset: 0.24,
                transform: `translate(${windupX}px, ${windupY}px) rotate(${-rotation * 0.5}deg) scale(0.97)`,
                filter: "brightness(1.05)",
            },
            {
                offset: 0.82,
                transform: activeAttack.heldTransform,
                filter: "brightness(1.22) saturate(1.15)",
            },
            { transform: activeAttack.heldTransform, filter: "brightness(1.12)" },
        ], {
            duration,
            easing: "cubic-bezier(.2,.78,.2,1)",
            fill: "forwards",
        });
        animation.id = "sticky-paws-attack-lunge";
        activeAttack.animation = animation;
        activeAttack.sourceInstance = String(sourceInstance);
        const lunging = activeAttack;
        animation.finished.then(() => {
            if (attacks.has(lunging)) returnAttacker(duration, lunging);
        }).catch(() => clearAttack(lunging));
    }

    function keepAttackSourceHidden() {
        for (const moving of attacks) {
            const current = critter(moving.sourceInstance);
            if (current && current !== moving.source) {
                moving.source.classList.remove("board-motion-source-hidden");
                moving.source = current;
            }
            hide(current);
        }
    }

    function returnAttacker(duration, returning = activeAttack) {
        if (!returning || returning.returning) return;
        keepAttackSourceHidden();
        returning.returning = true;
        const currentTransform = getComputedStyle(returning.clone).transform;
        const currentFilter = getComputedStyle(returning.clone).filter;
        const home = returning.source.getBoundingClientRect();
        const homeMatrix = new DOMMatrixReadOnly(getComputedStyle(returning.source).transform);
        const x = home.left + home.width / 2 - returning.rect.left - returning.rect.width / 2;
        const y = home.top + home.height / 2 - returning.rect.top - returning.rect.height / 2;
        const homeTransform = `matrix(${homeMatrix.a}, ${homeMatrix.b}, ${homeMatrix.c}, ${homeMatrix.d}, ${x}, ${y})`;
        const animation = returning.clone.animate([
            { transform: currentTransform, filter: currentFilter, opacity: 1 },
            { transform: homeTransform, filter: "brightness(1)", opacity: returning.source.isConnected ? 1 : 0 },
        ], {
            duration: Math.max(180, duration * 0.72),
            easing: "cubic-bezier(.34,.02,.2,1)",
            fill: "forwards",
        });
        animation.id = "sticky-paws-attack-return";
        function finishReturn() {
            const destination = critter(returning.sourceInstance);
            if (!destination || alignForHandoff(returning, destination, finishReturn))
                clearAttack(returning);
        }
        animation.finished.then(finishReturn).catch(() => clearAttack(returning));
    }

    // Blazor can replace attributes or a destination between beats. Reconcile before the browser paints,
    // retaining the travelling face until its real destination exists and never revealing a stale hand copy.
    function reconcile() {
        requestAnimationFrame(fitTooltips);
        for (const back of retiredOpponentBacks) {
            if (back.isConnected) hide(back);
            else retiredOpponentBacks.delete(back);
        }
        for (const instance of retiredHands) {
            const hand = document.querySelector(`[data-testid="hand-card"][data-instance="${instance}"]`);
            if (hand) hide(hand);
            else retiredHands.delete(instance);
        }
        keepAttackSourceHidden();
        for (const moving of activePlays) {
            if (moving.isTrick) {
                const receipt = document.querySelector(`[data-testid="last-trick"][data-instance="${moving.sourceInstance}"]`);
                if (receipt) {
                    moving.destinations.add(receipt);
                    hide(receipt);
                }
            }
            if (!moving.isCritter) continue;
            const destination = critter(moving.sourceInstance);
            if (destination) {
                moving.destinations.add(destination);
                if (!moving.morphed)
                    morphToBoard(moving, destination, moving.duration);
                // The entry socket is a preview that Blazor will replace at the beat boundary. Keep the
                // moving owner through that replacement; its settled face is already the destination face.
                if (moving.landed && moving.morphFinished
                    && !destination.closest('[data-testid="critter-entry-slot"]')) {
                    if (alignForHandoff(moving, destination, reconcile)) clearPlay(moving);
                    else hide(destination);
                } else hide(destination);
            }
        }
        if (!document.querySelector('[data-testid="match-board"]')) {
            clearPlay();
            clearAttack();
            retiredHands.clear();
            retiredOpponentBacks.clear();
        }
    }

    new MutationObserver(reconcile).observe(document.body, {
        subtree: true, childList: true, attributes: true, attributeFilter: ["class"],
    });

    window.stickyPawsBoardMotion = {
        // The one thing C# cannot answer for itself. The CSS and this file both honour the preference on
        // their own, but the beat queue is C#'s, and a reduced-motion player who still waits out every beat
        // has had the motion removed without the pacing -- which is not what client.md promises.
        prefersReducedMotion() {
            return window.matchMedia("(prefers-reduced-motion: reduce)").matches;
        },
        reset() {
            clearPlay();
            clearAttack();
            for (const source of document.querySelectorAll(".board-motion-source-hidden"))
                source.classList.remove("board-motion-source-hidden");
            retiredHands.clear();
            retiredOpponentBacks.clear();
        },
        run(sequence, kind, source, targetKind, target, duration, suppressImpact, opponent) {
            if (window.matchMedia("(prefers-reduced-motion: reduce)").matches) {
                clearPlay();
                clearAttack();
                for (const source of document.querySelectorAll(".board-motion-source-hidden"))
                    source.classList.remove("board-motion-source-hidden");
                retiredHands.clear();
                retiredOpponentBacks.clear();
                return;
            }
            if (kind === "none") {
                for (const moving of activePlays) {
                    if (moving.isCritter && !critter(moving.sourceInstance)
                        && !document.querySelector(`[data-entry-instance="${moving.sourceInstance}"]`))
                        clearPlay(moving);
                }
            }
            if (kind === "cardplay") {
                playCard(source, targetKind, target, duration, opponent);
                return;
            }
            if (kind === "attack") {
                attack(source, targetKind, target, duration);
                return;
            }
            if (!activeAttack) return;

            keepAttackSourceHidden();
            if ((kind === "damage" && suppressImpact) || kind === "dendamage") {
                returnAttacker(duration);
                return;
            }
            if (!["damage", "bubble", "reveal", "death", "stats", "keywords"].includes(kind))
                returnAttacker(duration);
        },
    };
}());

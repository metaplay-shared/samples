/**
 * The drooping bridges: the presentation-only spring chain each runner's weight sags, the chasm shadow and the
 * waterfall under the open span, and the logs and ropes drawn along the animated curve.
 */

import { finiteOr } from '../util.js';

/** Clear gorge air between the collision deck and waterfall crest. */
const WATERFALL_DECK_GAP = 3.2;
const WATERFALL_BODY_TILE_METRES = 4;
const WATERFALL_CREST_HEIGHT_METRES = 0.72;
const WATERFALL_FRAME_MS = 240;
const WATERFALL_CREST_SOURCE = { x: 0, y: 288, w: 2172, h: 228 };

/** Resting curve offset below the unchanged collision surface. */
function bridgeRestOffset(bridge, t) {
    const clamped = Math.max(0, Math.min(1, t));
    return -bridge.restSag * 4 * clamped * (1 - clamped);
}

/** Endpoints are immovable; the next few logs gain mobility gradually toward the centre. */
function bridgeAnchorMobility(index, count) {
    if (index <= 0 || index >= count - 1)
        return 0;
    const t = index / (count - 1);
    return Math.pow(Math.sin(Math.PI * t), 2.5);
}

/** Linear interpolation of the spring chain at normalized bridge position. */
function bridgeDynamicOffset(bridge, t) {
    const scaled = Math.max(0, Math.min(1, t)) * (bridge.logCount - 1);
    const left = Math.min(bridge.logCount - 1, Math.floor(scaled));
    const right = Math.min(bridge.logCount - 1, left + 1);
    const fraction = scaled - left;
    return bridge.offsets[left] * (1 - fraction) + bridge.offsets[right] * fraction;
}

/** Render-only vertical offset that lets a visible runner stand on the cosmetic curve. */
export function bridgeSurfaceOffsetAt(view, x) {
    for (const bridge of view.droopingBridges) {
        if (x < bridge.startX || x > bridge.endX)
            continue;
        const t = (x - bridge.startX) / (bridge.endX - bridge.startX);
        return bridgeRestOffset(bridge, t) + bridgeDynamicOffset(bridge, t);
    }
    return 0;
}

/** A finished runner is on the ground past the finish line, where there is no deck to load. */
function runnerLoadsBridge(mode, finished) {
    return !finished && mode !== 'Airborne' && mode !== 'Finished';
}

/** Presentation springs; these values never feed the fixed-step simulation or its recording. */
export function updateDroopingBridges(view, state, dt, restarted) {
    let anyLoaded = false;
    let maxMovement = 0;
    let maxEndpointMovement = 0;
    let maxAnchorZoneMovement = 0;
    let maxCenterMovement = 0;
    const playerX = finiteOr(state.x, view.startX);
    const runners = [{
        x: playerX,
        loads: runnerLoadsBridge(typeof state.mode === 'string' ? state.mode : 'Running', state.finished === true),
    }];
    const poolGhosts = Array.isArray(state.ghosts) ? state.ghosts : [];
    for (const ghost of poolGhosts) {
        // An opponent that is not on screen does not stand on a deck either, so the sag stays with what
        // the player can see on it.
        if (ghost === null || typeof ghost !== 'object' || ghost.hidden === true)
            continue;
        runners.push({
            x: finiteOr(ghost.x, view.startX),
            loads: runnerLoadsBridge(typeof ghost.mode === 'string' ? ghost.mode : 'Running', ghost.finished === true),
        });
    }

    for (const bridge of view.droopingBridges) {
        if (restarted) {
            bridge.offsets.fill(0);
            bridge.velocities.fill(0);
        }

        const targets = new Array(bridge.logCount).fill(0);
        for (const runner of runners) {
            if (!runner.loads || runner.x < bridge.startX || runner.x > bridge.endX)
                continue;
            anyLoaded = true;
            for (let index = 0; index < bridge.logCount; index++) {
                const nodeX = bridge.startX + (bridge.endX - bridge.startX) * index / (bridge.logCount - 1);
                const distance = Math.abs(nodeX - runner.x);
                if (distance >= bridge.responseRadius)
                    continue;
                const influence = 0.5 + 0.5 * Math.cos(Math.PI * distance / bridge.responseRadius);
                const mobility = bridgeAnchorMobility(index, bridge.logCount);
                targets[index] = Math.min(targets[index], -bridge.maxLocalDroop * influence * mobility);
            }
        }

        if (view.reducedMotion) {
            const blend = Math.min(1, dt * 18);
            for (let index = 0; index < bridge.logCount; index++) {
                if (index === 0 || index === bridge.logCount - 1) {
                    bridge.offsets[index] = 0;
                    bridge.velocities[index] = 0;
                    continue;
                }
                bridge.offsets[index] += (targets[index] * 0.45 - bridge.offsets[index]) * blend;
                bridge.velocities[index] = 0;
            }
        } else {
            const nextOffsets = bridge.offsets.slice();
            const nextVelocities = bridge.velocities.slice();
            for (let index = 0; index < bridge.logCount; index++) {
                if (index === 0 || index === bridge.logCount - 1) {
                    nextOffsets[index] = 0;
                    nextVelocities[index] = 0;
                    continue;
                }
                const mobility = bridgeAnchorMobility(index, bridge.logCount);
                const left = bridge.offsets[index - 1];
                const right = bridge.offsets[index + 1];
                const neighborPull = left + right - 2 * bridge.offsets[index];
                const stiffness = 64 + (1 - mobility) * 96;
                const acceleration = (targets[index] - bridge.offsets[index]) * stiffness
                    + neighborPull * 8 * mobility
                    - bridge.velocities[index] * 18;
                nextVelocities[index] += acceleration * dt;
                nextOffsets[index] += nextVelocities[index] * dt;
                nextOffsets[index] = Math.max(-bridge.maxLocalDroop * mobility,
                    Math.min(bridge.maxLocalDroop * 0.015 * mobility, nextOffsets[index]));
            }
            bridge.offsets = nextOffsets;
            bridge.velocities = nextVelocities;
        }

        for (let index = 0; index < bridge.logCount; index++) {
            const movement = Math.abs(bridge.offsets[index]);
            maxMovement = Math.max(maxMovement, movement);
            if (index === 0 || index === bridge.logCount - 1)
                maxEndpointMovement = Math.max(maxEndpointMovement, movement);
            if (index <= 2 || index >= bridge.logCount - 3)
                maxAnchorZoneMovement = Math.max(maxAnchorZoneMovement, movement);
            if (bridgeAnchorMobility(index, bridge.logCount) > 0.9)
                maxCenterMovement = Math.max(maxCenterMovement, movement);
        }
    }

    if (restarted) {
        view.bridgeWasLoaded = false;
        view.bridgeReboundUntilMs = 0;
    }
    if (view.bridgeWasLoaded && !anyLoaded)
        view.bridgeReboundUntilMs = view.animMs + 1200;
    view.bridgeWasLoaded = anyLoaded;

    view.testData.bridgeState = anyLoaded ? 'loaded'
        : maxMovement > 0.035 || view.animMs < view.bridgeReboundUntilMs ? 'rebounding'
            : view.droopingBridges.length > 0 ? 'resting' : 'absent';
    view.testData.bridgeEndpointDeformation = maxEndpointMovement.toFixed(3);
    view.testData.bridgeAnchorZoneDeformation = maxAnchorZoneMovement.toFixed(3);
    view.testData.bridgeCenterDeformation = maxCenterMovement.toFixed(3);
}

/** Shadow below the visually open span. Collision remains the uninterrupted authored ground. */
export function drawBridgeChasms(view) {
    const leftX = view.viewLeftX();
    const rightX = view.viewRightX();
    for (const bridge of view.droopingBridges) {
        if (bridge.endX < leftX || bridge.startX > rightX)
            continue;
        const x0 = view.alignPx(view.screenX(bridge.startX));
        const x1 = view.alignPx(view.screenX(bridge.endX));
        const y0 = view.alignPx(view.screenY(bridge.collisionY) + view.pxPerMetre * 0.25);
        const shadow = view.ctx.createLinearGradient(0, y0, 0, view.viewH);
        shadow.addColorStop(0, 'rgba(3, 12, 29, 0.18)');
        shadow.addColorStop(0.45, 'rgba(2, 8, 22, 0.54)');
        shadow.addColorStop(1, 'rgba(1, 4, 14, 0.9)');
        view.ctx.fillStyle = shadow;
        view.ctx.fillRect(x0, y0, Math.max(1, x1 - x0), view.viewH - y0);
    }
}

/**
 * A single tile wall behind the bridge and every runner; its base stays below the viewport. Its
 * translucency is the art's own.
 */
export function drawBridgeWaterfalls(view) {
    const frameIndex = view.reducedMotion ? 0 : Math.floor(view.animMs / WATERFALL_FRAME_MS) % 3;
    const assetReady = view.waterfallBodyArt.ready && view.waterfallCrestArt.ready;
    for (const bridge of view.droopingBridges) {
        if (!assetReady || !bridge.hasWaterfall || bridge.endX < view.viewLeftX() || bridge.startX > view.viewRightX())
            continue;

        const leftX = view.alignPx(view.screenX(bridge.startX));
        const rightX = view.alignPx(view.screenX(bridge.endX));
        const crestY = view.screenY(bridge.collisionY) + view.pxPerMetre * WATERFALL_DECK_GAP;
        const bodyTileSize = Math.max(1, Math.round(view.pxPerMetre * WATERFALL_BODY_TILE_METRES));
        const bodyOffset = Math.round(frameIndex * bodyTileSize / 3);
        const fillBottom = view.viewH + bodyTileSize;
        const firstBodyY = Math.floor((crestY - bodyOffset) / bodyTileSize) * bodyTileSize + bodyOffset;
        const crestHeight = Math.max(1, Math.round(view.pxPerMetre * WATERFALL_CREST_HEIGHT_METRES));
        const crestWidth = Math.max(1, Math.round(
            crestHeight * WATERFALL_CREST_SOURCE.w / WATERFALL_CREST_SOURCE.h));

        view.ctx.save();
        view.ctx.beginPath();
        view.ctx.rect(leftX, crestY, Math.max(1, rightX - leftX), Math.max(1, view.viewH - crestY));
        view.ctx.clip();
        view.ctx.imageSmoothingEnabled = false;
        for (let y = firstBodyY - bodyTileSize; y < fillBottom; y += bodyTileSize) {
            for (let x = leftX; x < rightX; x += bodyTileSize) {
                view.ctx.drawImage(view.waterfallBodyArt.image, 0, 0,
                    view.waterfallBodyArt.image.width, view.waterfallBodyArt.image.height,
                    x, y, bodyTileSize + 1, bodyTileSize + 1);
            }
        }
        for (let x = leftX; x < rightX; x += crestWidth) {
            view.ctx.drawImage(view.waterfallCrestArt.image,
                WATERFALL_CREST_SOURCE.x, WATERFALL_CREST_SOURCE.y,
                WATERFALL_CREST_SOURCE.w, WATERFALL_CREST_SOURCE.h,
                x, crestY, crestWidth + 1, crestHeight);
        }
        view.ctx.restore();
    }
    view.testData.bridgeWaterfallAssetReady = assetReady ? 'true' : 'false';
    view.testData.bridgeWaterfallFrameIndex = String(frameIndex);
    view.testData.bridgeWaterfallFrameBlend = '0.000';
}

function bridgeDrawPoints(view, bridge) {
    const points = [];
    for (let index = 0; index < bridge.logCount; index++) {
        const t = index / (bridge.logCount - 1);
        const worldX = bridge.startX + (bridge.endX - bridge.startX) * t;
        const surfaceY = bridge.collisionY + bridgeRestOffset(bridge, t) + bridge.offsets[index];
        points.push({ x: view.screenX(worldX), y: view.screenY(surfaceY), worldX, surfaceY });
    }
    return points;
}

/** A smooth path through discrete spring nodes, used for both load-bearing ropes. */
function traceBridgeCurve(view, points, yOffset) {
    if (points.length === 0)
        return;
    const offsetAt = index => {
        const t = index / (points.length - 1);
        return yOffset * Math.sin(Math.PI * t);
    };
    view.ctx.beginPath();
    view.ctx.moveTo(points[0].x, points[0].y);
    for (let index = 1; index + 1 < points.length; index++) {
        const midX = (points[index].x + points[index + 1].x) * 0.5;
        const midY = (points[index].y + offsetAt(index)
            + points[index + 1].y + offsetAt(index + 1)) * 0.5;
        view.ctx.quadraticCurveTo(points[index].x, points[index].y + offsetAt(index), midX, midY);
    }
    const last = points[points.length - 1];
    view.ctx.lineTo(last.x, last.y);
}

function drawBridgeAnchor(view, x, collisionY, facesRight) {
    const baseX = view.screenX(x);
    const baseY = view.screenY(collisionY);
    const postW = Math.max(5, view.pxPerMetre * 0.48);
    const postH = view.pxPerMetre * 1.75;
    view.ctx.fillStyle = '#17120f';
    view.ctx.fillRect(view.alignPx(baseX - postW * 0.5), view.alignPx(baseY - postH), postW, postH);
    view.ctx.fillStyle = '#765033';
    view.ctx.fillRect(view.alignPx(baseX - postW * 0.34), view.alignPx(baseY - postH), postW * 0.68, postH);
    view.ctx.strokeStyle = '#c69a5b';
    view.ctx.lineWidth = Math.max(2, view.pxPerMetre * 0.12);
    for (let wrap = 0; wrap < 3; wrap++) {
        const wrapY = baseY - postH * (0.62 - wrap * 0.11);
        view.ctx.beginPath();
        view.ctx.moveTo(baseX - postW * 0.58, wrapY);
        view.ctx.lineTo(baseX + postW * 0.58, wrapY + (facesRight ? 1 : -1));
        view.ctx.stroke();
    }
}

/** Repeated round logs and two continuous ropes along the animated curve. */
export function drawDroopingBridges(view) {
    const leftX = view.viewLeftX();
    const rightX = view.viewRightX();
    let visibleCount = 0;
    for (const bridge of view.droopingBridges) {
        if (bridge.endX < leftX || bridge.startX > rightX)
            continue;
        visibleCount++;
        const points = bridgeDrawPoints(view, bridge);
        const logSpacing = (bridge.endX - bridge.startX) / (bridge.logCount - 1);
        const logRadius = Math.max(3, view.pxPerMetre * Math.min(0.43, logSpacing * 0.42));

        view.ctx.save();
        view.ctx.lineCap = 'round';
        view.ctx.lineJoin = 'round';
        traceBridgeCurve(view, points, -logRadius * 0.25);
        view.ctx.strokeStyle = '#3a2418';
        view.ctx.lineWidth = Math.max(4, view.pxPerMetre * 0.24);
        view.ctx.stroke();
        traceBridgeCurve(view, points, -logRadius * 0.25);
        view.ctx.strokeStyle = '#c29457';
        view.ctx.lineWidth = Math.max(2, view.pxPerMetre * 0.095);
        view.ctx.stroke();

        for (let index = 0; index < points.length; index++) {
            const point = points[index];
            const previous = points[Math.max(0, index - 1)];
            const next = points[Math.min(points.length - 1, index + 1)];
            const angle = Math.atan2(next.y - previous.y, next.x - previous.x);
            view.ctx.save();
            view.ctx.translate(point.x, point.y + logRadius * 1.08);
            view.ctx.rotate(angle * 0.18);
            view.ctx.fillStyle = '#241914';
            view.ctx.beginPath();
            view.ctx.arc(0, 0, logRadius * 1.08, 0, Math.PI * 2);
            view.ctx.fill();
            view.ctx.fillStyle = index % 2 === 0 ? '#765033' : '#68452e';
            view.ctx.beginPath();
            view.ctx.arc(0, 0, logRadius * 0.9, 0, Math.PI * 2);
            view.ctx.fill();
            view.ctx.strokeStyle = '#a87945';
            view.ctx.lineWidth = Math.max(1, view.pxPerMetre * 0.055);
            view.ctx.beginPath();
            view.ctx.arc(0, 0, logRadius * 0.58, 0, Math.PI * 2);
            view.ctx.stroke();
            view.ctx.strokeStyle = '#3c281d';
            view.ctx.beginPath();
            view.ctx.arc(0, 0, logRadius * 0.28, 0, Math.PI * 2);
            view.ctx.stroke();
            view.ctx.restore();
        }

        traceBridgeCurve(view, points, logRadius * 0.55);
        view.ctx.strokeStyle = '#402719';
        view.ctx.lineWidth = Math.max(4, view.pxPerMetre * 0.22);
        view.ctx.stroke();
        traceBridgeCurve(view, points, logRadius * 0.55);
        view.ctx.strokeStyle = '#d2a564';
        view.ctx.lineWidth = Math.max(2, view.pxPerMetre * 0.085);
        view.ctx.stroke();
        view.ctx.restore();

        drawBridgeAnchor(view, bridge.startX, bridge.collisionY, true);
        drawBridgeAnchor(view, bridge.endX, bridge.collisionY, false);
    }
    view.testData.bridgeVisibleCount = String(visibleCount);
}

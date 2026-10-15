/**
 * The Orbs an accepted run paid, carried from the score screen into the main menu medallion's orbit.
 *
 * Components/OrbFlightLayer.razor owns the flight: which Orbs exist, where each one starts, and how many are still
 * uncounted. It renders them as `[data-testid="flying-orb"]` elements in the flight layer, a sibling of the screen
 * transition that sits above it, each placed at the reward mark that paid it. This module moves them, with
 * one requestAnimationFrame loop for the whole flight, in three stages:
 *
 * 1. `burst`: as the score screen is dismissed, each Orb leaves its mark in a random direction at a random
 *    speed and slows into a gentle, wobbling float that lasts through the curtain's cover, swap and reveal.
 *    The float is kept inside the portrait area by a soft pull back from its edges.
 * 2. `gather`: once the menu is uncovered, the Orbs set off one after another and accelerate from wherever
 *    they have drifted towards the ring's centre, each stopping where its path meets the orbit circle.
 * 3. On arrival, the screen angle of the landing point is reported to C# as
 *    `OnOrbLanded(flight, orbId, angleDegrees)`, which renders a pearl that starts its own orbit from that
 *    angle. Until the Orb's element is taken out of the layer it holds still on the landing point, which is
 *    where the pearl appears.
 *
 * Positions are in the flight layer's own pixels (its top-left corner is 0,0), which is also the portrait
 * area's, since the layer fills it. Only one flight is ever in the air.
 */

import { clamp } from './util.js';

/** The fastest and slowest an Orb leaves its reward mark, in pixels per second. */
const BURST_SPEED_MIN = 320;
const BURST_SPEED_MAX = 640;

/** How quickly the burst gives way to the float: the time constant of the slow-down, in seconds. */
const BURST_SETTLE_S = 0.32;

/** The float's own speed, in pixels per second, and how fast its heading wanders, in radians per second. */
const DRIFT_SPEED = 16;
const DRIFT_TURN_RATE = 0.9;

/** The wobble laid over the float: its size in pixels and its rate in radians per second. */
const WOBBLE_PX = 3;
const WOBBLE_RATE = 2.6;

/**
 * How far inside the portrait area's edges the float turns back, how hard it is pulled back once past that
 * line (per pixel past it, per second squared), and the hard limit it can never cross.
 */
const EDGE_MARGIN_PX = 28;
const EDGE_PULL = 60;
const EDGE_LIMIT_PX = 6;

/** Delay between one Orb setting off for the orbit and the next, so they stream in rather than arrive as one. */
const STAGGER_MS = 90;

/** How long one Orb takes to reach the ring: a base plus a share of the distance, within these bounds. */
const GATHER_BASE_MS = 360;
const GATHER_MS_PER_PX = 0.9;
const GATHER_MIN_MS = 420;
const GATHER_MAX_MS = 900;

/** The ease of the gather: position goes as progress to this power, so the Orb is pulled in ever faster. */
const GATHER_EASE_POWER = 2.4;

/** A pearl's size relative to a flying Orb, which is what an Orb shrinks to on its way in. */
const LANDED_SCALE = 0.62;

/** How long a landed Orb holds on its landing point for C# to take its element out before the loop lets it go anyway. */
const LANDED_HOLD_MS = 1500;

/** The flight in the air, or null. */
let current = null;

/**
 * Where each score-screen reward mark is, as a flat list of triples: the segment it belongs to, then the
 * x and y of the centre of its orb glyph. A reward with no glyph is measured at its own centre.
 */
export function measureRewardMarks(layer) {
    const area = layer.getBoundingClientRect();
    const rewards = layer.parentElement?.querySelectorAll('[data-testid="gate-reward"]') ?? [];
    const marks = [];
    for (const reward of rewards) {
        const glyph = reward.querySelector('.orb-mark') ?? reward;
        const box = glyph.getBoundingClientRect();
        marks.push(Number(reward.dataset.segment ?? marks.length / 3));
        marks.push(box.left + box.width / 2 - area.left);
        marks.push(box.top + box.height / 2 - area.top);
    }
    return marks;
}

/**
 * Burst the Orbs of `flight` from their marks and let them float. `starts` is a flat list of triples: the
 * Orb's id, then the x and y it is rendered at. Their elements may not be in the layer yet; each is looked
 * up as the loop first needs it.
 */
export function burst(layer, flight, starts) {
    cancel();

    const orbs = [];
    for (let ndx = 0; ndx + 2 < starts.length; ndx += 3) {
        const heading = Math.random() * Math.PI * 2;
        const speed = BURST_SPEED_MIN + Math.random() * (BURST_SPEED_MAX - BURST_SPEED_MIN);
        orbs.push({
            id: starts[ndx],
            element: null,
            startX: starts[ndx + 1],
            startY: starts[ndx + 2],
            x: starts[ndx + 1],
            y: starts[ndx + 2],
            vx: Math.cos(heading) * speed,
            vy: Math.sin(heading) * speed,
            driftHeading: Math.random() * Math.PI * 2,
            driftTurn: (Math.random() < 0.5 ? -1 : 1) * DRIFT_TURN_RATE * (0.5 + Math.random()),
            wobblePhase: Math.random() * Math.PI * 2,
            wobbleAge: 0,
            stage: 'drift',
        });
    }

    current = { layer, flight, owner: null, orbs, frame: 0, lastTime: 0, ring: null };
    current.frame = requestAnimationFrame(step);
}

/**
 * Send the floating Orbs of `flight` into the medallion's orbit, one after another. `owner` is told
 * `OnOrbLanded(flight, orbId, angleDegrees)` as each one arrives, with the screen angle of the landing point
 * clockwise from straight up, or null for the angle when there is no ring to land on.
 */
export function gather(layer, owner, flight) {
    if (!current || current.flight !== flight) {
        // Nothing is floating for this flight: the Orbs are counted where they are.
        for (const element of layer.querySelectorAll('[data-testid="flying-orb"]'))
            report(owner, flight, Number(element.dataset.orbId), null);
        return;
    }

    current.owner = owner;
    const ring = layer.parentElement?.querySelector('[data-testid="orbit-ring"]');
    if (!ring) {
        for (const orb of current.orbs)
            report(owner, flight, orb.id, null);
        cancel();
        return;
    }

    current.ring = ring;
    const now = performance.now();
    current.orbs.forEach((orb, index) => {
        orb.gatherAt = now + index * STAGGER_MS;
    });
}

/** Stop every Orb still in the air. The C# side has already counted them and emptied the layer. */
export function cancel() {
    if (current)
        cancelAnimationFrame(current.frame);
    current = null;
}

function step(time) {
    const flight = current;
    if (!flight)
        return;

    const dt = flight.lastTime === 0 ? 0 : Math.min(0.05, (time - flight.lastTime) / 1000);
    flight.lastTime = time;

    const width = flight.layer.clientWidth;
    const height = flight.layer.clientHeight;
    const orbit = flight.ring ? measureOrbit(flight.layer, flight.ring) : null;
    const now = performance.now();

    let active = 0;
    for (const orb of flight.orbs) {
        if (orb.stage === 'gone')
            continue;

        if (!orb.element || !orb.element.isConnected) {
            const found = flight.layer.querySelector(`[data-orb-id="${orb.id}"]`);
            if (!found && orb.element) {
                // C# has taken the Orb out of the layer; it is a pearl now.
                orb.stage = 'gone';
                continue;
            }
            orb.element = found;
        }

        active++;
        if (orb.stage === 'drift' && orbit && orb.gatherAt !== undefined && now >= orb.gatherAt)
            beginGather(orb, orbit);

        if (orb.stage === 'drift')
            drift(orb, dt, width, height);
        else if (orb.stage === 'gather')
            gatherStep(flight, orb, orbit, now);
        else if (orb.stage === 'landed')
            landedStep(orb, now);

        place(orb);
    }

    if (active > 0)
        flight.frame = requestAnimationFrame(step);
    else
        current = null;
}

/** The float: the burst's speed decays into a slow wandering drift, pulled back from the area's edges. */
function drift(orb, dt, width, height) {
    orb.driftHeading += orb.driftTurn * dt;
    const targetVx = Math.cos(orb.driftHeading) * DRIFT_SPEED;
    const targetVy = Math.sin(orb.driftHeading) * DRIFT_SPEED;
    const blend = 1 - Math.exp(-dt / BURST_SETTLE_S);
    orb.vx += (targetVx - orb.vx) * blend;
    orb.vy += (targetVy - orb.vy) * blend;

    orb.vx += edgePull(orb.x, orb.vx, width) * dt;
    orb.vy += edgePull(orb.y, orb.vy, height) * dt;

    orb.x = clamp(orb.x + orb.vx * dt, EDGE_LIMIT_PX, width - EDGE_LIMIT_PX);
    orb.y = clamp(orb.y + orb.vy * dt, EDGE_LIMIT_PX, height - EDGE_LIMIT_PX);
    orb.wobbleAge += dt;

    const wobble = orb.wobblePhase + orb.wobbleAge * WOBBLE_RATE;
    orb.renderX = orb.x + Math.sin(wobble) * WOBBLE_PX;
    orb.renderY = orb.y + Math.cos(wobble * 0.73) * WOBBLE_PX;
    orb.scale = 1;
}

/** The acceleration back towards the inside for one axis, and a brake on any speed still heading out. */
function edgePull(position, velocity, extent) {
    const low = EDGE_MARGIN_PX - position;
    if (low > 0)
        return low * EDGE_PULL - (velocity < 0 ? velocity * 6 : 0);
    const high = position - (extent - EDGE_MARGIN_PX);
    if (high > 0)
        return -high * EDGE_PULL - (velocity > 0 ? velocity * 6 : 0);
    return 0;
}

/**
 * Aim an Orb at the point where the line from it to the ring's centre crosses the orbit circle, and start
 * pulling it in from exactly where it is rendered.
 */
function beginGather(orb, orbit) {
    const fromX = orb.renderX ?? orb.x;
    const fromY = orb.renderY ?? orb.y;
    let dx = fromX - orbit.centreX;
    let dy = fromY - orbit.centreY;
    let length = Math.hypot(dx, dy);
    if (length < 0.5) {
        const angle = Math.random() * Math.PI * 2;
        dx = Math.sin(angle);
        dy = -Math.cos(angle);
        length = 1;
    }

    orb.unitX = dx / length;
    orb.unitY = dy / length;
    orb.fromX = fromX;
    orb.fromY = fromY;
    const distance = Math.abs(length - orbit.radius);
    orb.gatherStart = performance.now();
    orb.gatherMs = clamp(GATHER_BASE_MS + distance * GATHER_MS_PER_PX, GATHER_MIN_MS, GATHER_MAX_MS);
    orb.stage = 'gather';
}

function gatherStep(flight, orb, orbit, now) {
    const targetX = orbit.centreX + orbit.radius * orb.unitX;
    const targetY = orbit.centreY + orbit.radius * orb.unitY;
    const progress = Math.min(1, (now - orb.gatherStart) / orb.gatherMs);
    const eased = Math.pow(progress, GATHER_EASE_POWER);

    orb.renderX = orb.fromX + (targetX - orb.fromX) * eased;
    orb.renderY = orb.fromY + (targetY - orb.fromY) * eased;
    orb.scale = 1 + (LANDED_SCALE - 1) * eased;

    if (progress < 1)
        return;

    // The screen angle of the landing point, clockwise from straight up as the pearls are laid out. The
    // ring itself does not turn, each pearl orbits on its own, so this is where the new pearl starts.
    const screenDegrees = Math.atan2(orb.unitX, -orb.unitY) * 180 / Math.PI;
    orb.stage = 'landed';
    orb.landedAt = now;
    report(flight.owner, flight.flight, orb.id, normaliseDegrees(screenDegrees));
}

/**
 * A landed Orb holds still on its landing point, where its pearl starts, until C# takes it out of the layer
 * on the next render.
 */
function landedStep(orb, now) {
    if (now - orb.landedAt > LANDED_HOLD_MS) {
        orb.stage = 'gone';
        return;
    }

    orb.scale = LANDED_SCALE;
}

/** Move an Orb's element to where the loop has it, relative to the spot C# rendered it at. */
function place(orb) {
    if (!orb.element || orb.renderX === undefined)
        return;
    const dx = orb.renderX - orb.startX;
    const dy = orb.renderY - orb.startY;
    orb.element.style.transform =
        `translate(-50%, -50%) translate(${dx.toFixed(2)}px, ${dy.toFixed(2)}px) scale(${orb.scale.toFixed(3)})`;
}

/**
 * The orbit as it stands on screen now: the ring's centre in the layer's pixels and the pearls' radius at the
 * medallion's live scale. The scale is read off the medallion button that carries the ring, which is scaled
 * as a whole on a short portrait area.
 */
function measureOrbit(layer, ring) {
    const area = layer.getBoundingClientRect();
    const box = ring.getBoundingClientRect();
    const shrine = ring.closest('.loadout-shrine');
    const scale = shrine && shrine.offsetWidth > 0 ? shrine.getBoundingClientRect().width / shrine.offsetWidth : 1;
    return {
        centreX: box.left + box.width / 2 - area.left,
        centreY: box.top + box.height / 2 - area.top,
        radius: orbitRadiusPx(ring) * scale,
    };
}

/**
 * The pearls' distance from the ring's centre at the medallion's unscaled size: the stylesheet's
 * `--orbit-radius`, which the pearls' own orbit is drawn at.
 */
function orbitRadiusPx(ring) {
    const radius = parseFloat(getComputedStyle(ring).getPropertyValue('--orbit-radius'));
    return Number.isFinite(radius) ? radius : 0;
}

function report(owner, flight, orbId, angleDegrees) {
    if (!owner)
        return;
    owner.invokeMethodAsync('OnOrbLanded', flight, orbId, angleDegrees).catch(() => {
        // The page has gone away; nobody is left to count it.
    });
}

function normaliseDegrees(degrees) {
    const wrapped = degrees % 360;
    return wrapped < 0 ? wrapped + 360 : wrapped;
}

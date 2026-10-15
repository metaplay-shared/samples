/**
 * The web client's sound: the run's own noises, the menu tap, and the mute switch they all obey.
 *
 * Everything plays through one shared `AudioContext`, created and resumed only from `unlock()` so the
 * browser's gesture requirement is satisfied by the press that starts a run (or by the button click that
 * opens one). Clips are fetched and decoded lazily on that same unlock, and each decoded buffer is kept for
 * the rest of the page's life, so a second run makes no further requests.
 *
 * The run itself never raises events: `onSnapshot` is handed the same per-frame snapshot the renderer draws
 * (`{ step, elapsedMs, x, y, vx, vy, mode, encounterPhase, charge, finished, finishTimeMs }`) and derives
 * what to play by comparing it against the previous one. The course's pits are handed over once, at
 * creation, which is all the geometry it needs: everything else is read off the snapshot. That keeps the
 * audio a pure reader of the simulation: nothing here can change what the run does or what time it
 * produces.
 *
 * The graph is:
 *
 *     one-shots ──┐
 *     charge loop ├─→ master gain ─→ destination
 *     footsteps   │      (0 while muted)
 *     ambient   ──┘
 *
 * so levels are tuned in the two tables below and mute is one value.
 */

import { clamp } from './util.js';

/** Where the clips live, relative to the base URL passed to `createTimeTrialAudio`. */
const CLIP_FILES = {
    charge:     'charge.sha256-1abe80910b0aebc5.wav',
    launch:     'launch.sha256-7503d66e3800d151.wav',
    land:       'land.sha256-a6695eecf4b5362c.wav',
    stumbleHit: 'stumble-hit.sha256-e74cdc5cbce37dc8.wav',
    // The drag of a mantle.
    scrape:     'mantle-scrape.sha256-8b8267a32c554083.wav',
    finish:     'finish-sting.sha256-40ff8dfded51b7f2.wav',
    footsteps:  'footsteps-loop.sha256-39fcdb4650a6c608.wav',
    ambient:    'ambient-loop.sha256-652036ddd7260296.wav',
    menuTap:    'menu-tap.sha256-208d1d782b02fc9f.wav',
};

/**
 * How each one-shot is voiced: the clip it plays, where it sits in the mix, and the rate it plays at. The
 * hit and the finish sting are the loudest things in the mix; the menu tap is a tick. Landing on the floor
 * of a pit is the landing clip dropped a long way, so the same touchdown reads as heavier the further it
 * fell.
 */
const ONE_SHOT_VOICES = {
    launch:       { clip: 'launch',     gain: 0.80 },
    land:         { clip: 'land',       gain: 0.65 },
    pitLand:      { clip: 'land',       gain: 0.95, rate: 0.68 },
    stumbleHit:   { clip: 'stumbleHit', gain: 1.00 },
    mantleScrape: { clip: 'scrape',     gain: 0.50, rate: 0.55 },
    finish:       { clip: 'finish',     gain: 1.00 },
    menuTap:      { clip: 'menuTap',    gain: 0.45 },
};

/** Loop levels. Ambience and footsteps sit under everything else; the charge meter is meant to be heard. */
const LOOP_GAINS = {
    charge:    0.55,
    footsteps: 0.20,
    ambient:   0.10,
};

/** The charge loop's playback rate at no charge and at full charge, the rise the meter is heard as. */
const CHARGE_RATE_MIN = 0.75;
const CHARGE_RATE_MAX = 1.65;

/** The runner's speed, in meters per second, that plays the footstep loop at its recorded rate. */
const FOOTSTEP_REFERENCE_SPEED = 8;

/** How long after a one-shot was asked for it may still start, once its clip has finished decoding. */
const ONE_SHOT_MAX_LATENCY_MS = 250;

/** How near a pit's floor the runner's feet must come down for the landing to be the one on its floor. */
const PIT_FLOOR_TOLERANCE_METRES = 0.05;

/** Where the mute switch is remembered between visits. Nothing stored means a first visit, which is muted. */
const MUTED_STORAGE_KEY = 'ninjarun.audio.muted';

/** The single context every sound shares, created on the first `unlock()`. */
let sharedContext = null;
let masterGain    = null;

/** Decoded clips, keyed by resolved URL. The value is the in-flight promise, so a clip is fetched once. */
const clipBuffers = new Map();

let muted = readStoredMuted();

/**
 * Whether sound is currently switched off. Read at startup so the button can be labelled before anything
 * has played.
 *
 * @returns {boolean}
 */
export function isMuted() {
    return muted;
}

/**
 * Switch sound off or back on and remember the choice. Applied as master gain, so loops keep running
 * silently and come back in place rather than restarting.
 *
 * @param {boolean} value true to mute
 * @returns {boolean} the flag as it now stands
 */
function setMuted(value) {
    muted = !!value;

    // Private browsing and blocked site data make storage throw; the switch still works for this visit.
    try {
        window.localStorage.setItem(MUTED_STORAGE_KEY, muted ? '1' : '0');
    } catch {
        // Nothing to do: the choice simply will not survive a reload.
    }

    applyMasterGain();
    return muted;
}

/**
 * Flip the mute switch.
 *
 * @returns {boolean} the flag as it now stands
 */
export function toggleMuted() {
    return setMuted(!muted);
}

/**
 * Play the menu tick. Called from a click, which is a user gesture, so this is also allowed to create and
 * resume the audio context: it is what warms the sound up before the first run.
 *
 * @param {string} [baseUrl] where the clips live, when the context has not been opened yet
 */
export function playMenuTap(baseUrl) {
    unlock(baseUrl);
    playOneShot('menuTap', baseUrl);
}

/**
 * Open the audio context and start pulling the clips in. Must be reached from a user gesture: a browser
 * refuses to start audio otherwise, and the context stays `suspended`.
 *
 * @param {string} [baseUrl] where the clips live
 */
export function unlock(baseUrl) {
    const context = ensureContext();
    if (!context)
        return;

    if (context.state === 'suspended')
        context.resume().catch(() => { /* A refused resume leaves the run silent, not broken. */ });

    preload(baseUrl);
}

/**
 * Create the sound for one run.
 *
 * `onSnapshot` is the whole interface to the simulation: hand it every frame and it works out what to play.
 * Nothing starts until `unlock()` has been reached from a user gesture.
 *
 * @param {{ baseUrl?: string, pits?: Array<{startX:number,endX:number,floorY:number,depth:number}> }} [options]
 *   `baseUrl` is where the clips are served from (default `audio/`); `pits` is the course's own list of
 *   shafts, which is how a touchdown on a pit floor is told from one on the ground.
 * @returns {{ unlock: () => void, onSnapshot: (snapshot: object) => void, dispose: () => void }}
 */
export function createTimeTrialAudio(options) {
    const baseUrl = (options && options.baseUrl) || 'audio/';

    const state = {
        baseUrl,
        pits:         Array.isArray(options && options.pits) ? options.pits : [],
        /** Set once the run has had its last sound, so late frames cannot start the loops again. */
        disposed:     false,
        previousMode: null,
        /** The last encounter phase seen, so a mantle scrapes once rather than on every frame of the rise. */
        previousEncounterPhase: '',
        /** Set once the finish sting has played, so the frames of the run-out past the line do not repeat it. */
        finishCued:   false,
        loops: {
            charge:    createLoop('charge'),
            footsteps: createLoop('footsteps'),
            ambient:   createLoop('ambient'),
        },
    };

    return {
        unlock:     () => unlock(baseUrl),
        onSnapshot: (snapshot) => onSnapshot(state, snapshot),
        dispose:    () => dispose(state),
    };
}

// ---------------------------------------------------------------------------------------------------------
// Snapshot → sound
// ---------------------------------------------------------------------------------------------------------

/**
 * One frame of the run. The mode transitions between this snapshot and the last are the events; the
 * continuous values (charge, horizontal speed) drive the loops that are already playing.
 */
function onSnapshot(state, snapshot) {
    if (state.disposed || !snapshot)
        return;

    const mode     = snapshot.mode || '';
    const previous = state.previousMode;
    const phase    = typeof snapshot.encounterPhase === 'string' ? snapshot.encounterPhase : '';

    // The ambience runs under the whole attempt, so it starts with the first frame that arrives.
    startLoop(state, 'ambient', 1);

    if (mode !== previous) {
        if (mode === 'Charging') {
            startLoop(state, 'charge', chargeRate(snapshot.charge), chargeGain(snapshot.charge));
        } else if (previous === 'Charging') {
            stopLoop(state, 'charge');

            // Only a charge that was let go of becomes a jump; one cut short by a hazard just stops.
            if (mode === 'Airborne')
                playOneShot('launch', state.baseUrl);
        }

        // A touchdown at the bottom of a shaft is the same beat, heavier: the fall that reached it was the
        // whole depth of the pit.
        if (previous === 'Airborne' && mode === 'Running')
            playOneShot(landedInPit(state, snapshot) ? 'pitLand' : 'land', state.baseUrl);

        if (mode === 'Stumbling')
            playOneShot('stumbleHit', state.baseUrl);

        state.previousMode = mode;
    }

    // The rise up a face is the only part of a mantle that moves the runner, so the scrape belongs to it,
    // once, where it starts, rather than on every frame of the climb.
    if (phase !== state.previousEncounterPhase) {
        if (phase === 'ledge-rise')
            playOneShot('mantleScrape', state.baseUrl);

        state.previousEncounterPhase = phase;
    }

    if (mode === 'Charging')
        setLoopRate(state, 'charge', chargeRate(snapshot.charge), chargeGain(snapshot.charge));

    // Footsteps belong to the run cycle only, and keep pace with it.
    if (mode === 'Running')
        startLoop(state, 'footsteps', footstepRate(snapshot.vx));
    else
        stopLoop(state, 'footsteps');

    // The sting marks the crossing, once. The runner runs on past the line afterwards, so the footsteps and
    // the ambience carry on under it until the run's sound is shut down with the loop.
    if (snapshot.finished && !state.finishCued) {
        state.finishCued = true;
        stopLoop(state, 'charge');
        playOneShot('finish', state.baseUrl);
    }
}

/** The charge loop's rate rises with the meter, so a fuller jump is heard winding up. */
function chargeRate(charge) {
    const t = clamp(charge ?? 0, 0, 1);
    return CHARGE_RATE_MIN + (CHARGE_RATE_MAX - CHARGE_RATE_MIN) * t;
}

/** ...and swells with it, so the meter is audible as well as visible. */
function chargeGain(charge) {
    const t = clamp(charge ?? 0, 0, 1);
    return LOOP_GAINS.charge * (0.45 + 0.55 * t);
}

/**
 * Whether the runner has just come down on the floor of a pit rather than on ordinary ground: their feet are
 * inside a shaft's span and at its floor, which is the one height the simulation can leave them at there.
 */
function landedInPit(state, snapshot) {
    const x = snapshot.x;
    const y = snapshot.y;
    if (!Number.isFinite(x) || !Number.isFinite(y))
        return false;

    for (const pit of state.pits) {
        if (x < pit.startX || x > pit.endX)
            continue;
        if (Math.abs(y - pit.floorY) <= PIT_FLOOR_TOLERANCE_METRES)
            return true;
    }

    return false;
}

/** Footsteps keep pace with the runner, within a range that still sounds like feet. */
function footstepRate(vx) {
    return clamp(Math.abs(vx ?? 0) / FOOTSTEP_REFERENCE_SPEED, 0.5, 2.0);
}

// ---------------------------------------------------------------------------------------------------------
// Voices
// ---------------------------------------------------------------------------------------------------------

function createLoop(name) {
    return { name, source: null, gain: null };
}

/** Start a loop if it is not already going, and set where it sits in the mix. */
function startLoop(state, name, rate, gainValue) {
    const loop    = state.loops[name];
    const context = sharedContext;
    if (!loop || !context || state.disposed)
        return;

    if (loop.source) {
        setLoopRate(state, name, rate, gainValue);
        return;
    }

    const buffer = clipBuffers.get(clipUrl(state.baseUrl, name))?.buffer;
    if (!buffer) {
        // Not decoded yet. Pull it in; the next frame starts the loop.
        loadClip(state.baseUrl, name);
        return;
    }

    loop.gain = context.createGain();
    loop.gain.gain.value = gainValue ?? LOOP_GAINS[name] ?? 0.5;
    loop.gain.connect(masterGain);

    loop.source = context.createBufferSource();
    loop.source.buffer = buffer;
    loop.source.loop = true;
    if (rate !== undefined)
        loop.source.playbackRate.value = rate;
    loop.source.connect(loop.gain);
    loop.source.start();
}

function setLoopRate(state, name, rate, gainValue) {
    const loop = state.loops[name];
    if (!loop || !loop.source)
        return;

    if (rate !== undefined)
        loop.source.playbackRate.value = rate;
    if (gainValue !== undefined && loop.gain)
        loop.gain.gain.value = gainValue;
}

function stopLoop(state, name) {
    const loop = state.loops[name];
    if (!loop || !loop.source)
        return;

    try {
        loop.source.stop();
    } catch {
        // A source that never started, or already ended, has nothing to stop.
    }

    loop.source.disconnect();
    loop.gain?.disconnect();
    loop.source = null;
    loop.gain   = null;
}

/** Silence this run's loops for good. One-shots already in flight ring out on their own. */
function dispose(state) {
    for (const name of Object.keys(state.loops))
        stopLoop(state, name);
    state.disposed = true;
}

/**
 * Play a cue once, as `ONE_SHOT_VOICES` voices it. The decode may still be in flight on the first frames of
 * the very first run, so the request is held until the buffer arrives, but dropped if that takes long
 * enough for the sound to be wrong, rather than firing a stale launch after the runner has landed.
 */
function playOneShot(name, baseUrl) {
    const context = sharedContext;
    if (!context)
        return;

    const voice = ONE_SHOT_VOICES[name];
    if (!voice)
        return;

    const url   = clipUrl(baseUrl, voice.clip);
    const entry = clipBuffers.get(url);

    if (entry?.buffer) {
        startOneShot(entry.buffer, voice.gain, voice.rate);
        return;
    }

    const requestedAt = now();
    loadClip(baseUrl, voice.clip).then((buffer) => {
        if (buffer && now() - requestedAt <= ONE_SHOT_MAX_LATENCY_MS)
            startOneShot(buffer, voice.gain, voice.rate);
    });
}

function startOneShot(buffer, gainValue, rate) {
    const context = sharedContext;
    if (!context || !masterGain)
        return;

    const gain = context.createGain();
    gain.gain.value = gainValue ?? 1;
    gain.connect(masterGain);

    const source = context.createBufferSource();
    source.buffer = buffer;
    if (rate !== undefined)
        source.playbackRate.value = rate;
    source.connect(gain);
    source.onended = () => {
        source.disconnect();
        gain.disconnect();
    };
    source.start();
}

// ---------------------------------------------------------------------------------------------------------
// Context and clips
// ---------------------------------------------------------------------------------------------------------

function ensureContext() {
    if (sharedContext)
        return sharedContext;

    const Ctor = window.AudioContext || window.webkitAudioContext;
    if (!Ctor)
        return null;

    sharedContext = new Ctor();

    masterGain = sharedContext.createGain();
    masterGain.connect(sharedContext.destination);

    applyMasterGain();
    return sharedContext;
}

function applyMasterGain() {
    if (masterGain)
        masterGain.gain.value = muted ? 0 : 1;
}

function preload(baseUrl) {
    for (const name of Object.keys(CLIP_FILES))
        loadClip(baseUrl, name);
}

function clipUrl(baseUrl, name) {
    return `${baseUrl || 'audio/'}${CLIP_FILES[name]}`;
}

/**
 * Fetch and decode one clip, once. The cache entry is filled in as soon as the buffer exists, so the hot
 * path (`clipBuffers.get(url).buffer`) never has to await.
 */
function loadClip(baseUrl, name) {
    const url = clipUrl(baseUrl, name);

    let entry = clipBuffers.get(url);
    if (entry)
        return entry.promise;

    entry = { buffer: null, promise: null };
    clipBuffers.set(url, entry);

    entry.promise = fetch(url)
        .then((response) => {
            if (!response.ok)
                throw new Error(`${response.status} ${response.statusText}`);
            return response.arrayBuffer();
        })
        .then((bytes) => decodeAudio(bytes))
        .then((buffer) => {
            entry.buffer = buffer;
            return buffer;
        })
        .catch((err) => {
            console.warn(`[timeTrialAudio] Could not load "${url}"; it stays silent.`, err);
            return null;
        });

    return entry.promise;
}

/** Safari's older `decodeAudioData` answers through callbacks rather than a promise, so both are covered. */
function decodeAudio(bytes) {
    const context = sharedContext;
    if (!context)
        return Promise.resolve(null);

    return new Promise((resolve, reject) => {
        const decoded = context.decodeAudioData(bytes, resolve, reject);
        if (decoded && typeof decoded.then === 'function')
            decoded.then(resolve, reject);
    });
}

function now() {
    return (typeof performance !== 'undefined' && performance.now) ? performance.now() : Date.now();
}

/**
 * The switch as the player last left it. The game starts muted (an autorunner that begins making noise on
 * its own is the wrong first impression) so only an explicit stored "0" turns sound on, and a browser that
 * refuses storage stays quiet.
 */
function readStoredMuted() {
    try {
        return window.localStorage.getItem(MUTED_STORAGE_KEY) !== '0';
    } catch {
        return true;
    }
}

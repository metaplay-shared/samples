/**
 * Small helpers the client's modules share.
 */

/**
 * Whether the page was opened by the end-to-end tests, which set `globalThis.hsrDiagnostics` before the page
 * loads. Only then do the run's host and renderer publish their state on the canvas as data attributes for
 * the tests to read, so a player's frame writes none.
 * @returns {boolean}
 */
export function diagnosticsEnabled() {
    return globalThis.hsrDiagnostics === true;
}

/**
 * Whether accessibility settings ask the app to keep motion to a minimum.
 * @returns {boolean}
 */
export function prefersReducedMotion() {
    return window.matchMedia?.('(prefers-reduced-motion: reduce)').matches ?? false;
}

/**
 * A value that is already a finite number, or `fallback` for anything else: a missing field, a string, NaN.
 * Nothing is converted.
 * @param {*} value
 * @param {number} fallback
 * @returns {number}
 */
export function finiteOr(value, fallback) {
    return typeof value === 'number' && Number.isFinite(value) ? value : fallback;
}

/**
 * `value` held within `min` .. `max`. A value that is not a finite number gives `min`.
 * @param {number} value
 * @param {number} min
 * @param {number} max
 * @returns {number}
 */
export function clamp(value, min, max) {
    if (!Number.isFinite(value))
        return min;
    return value < min ? min : (value > max ? max : value);
}

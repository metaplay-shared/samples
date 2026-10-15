/**
 * Image loading.
 *
 * Images are cached per URL for the whole page, so a second renderer over the same art starts with
 * everything already decoded. `render` never waits: it draws whatever has loaded. A run that wants the whole
 * stage before it is shown waits on `whenReady` instead.
 */

/** @type {Map<string, { image: HTMLImageElement, ready: boolean, promise: Promise<void> }>} */
const imageCache = new Map();

/**
 * Returns the cache entry for an image URL, starting the load on first use. `ready` turns true once the image
 * has arrived and been decoded, not merely arrived: the first `drawImage` of an undecoded image decodes it
 * there and then, on the main thread, a hitch in whichever frame it lands in. `promise` settles at the same
 * moment, and also for an image that never arrives, since the run draws without one; it never rejects.
 * @param {string} url
 * @returns {{ image: HTMLImageElement, ready: boolean, promise: Promise<void> }}
 */
export function loadImage(url) {
    let entry = imageCache.get(url);
    if (entry !== undefined)
        return entry;

    const image = new Image();
    entry = { image: image, ready: false, promise: null };
    image.decoding = 'async';
    entry.promise = new Promise((resolve) => {
        image.addEventListener('load', () => {
            // A decode the browser refuses is left to drawImage after all.
            const decoded = typeof image.decode === 'function' ? image.decode().catch(() => {}) : Promise.resolve();
            decoded.then(() => {
                entry.ready = true;
                resolve();
            });
        });
        image.addEventListener('error', () => { resolve(); });
    });
    image.src = url;
    imageCache.set(url, entry);
    return entry;
}

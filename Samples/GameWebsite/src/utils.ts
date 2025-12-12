// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

function isScriptLoaded(src: string): boolean {
  return document.head.querySelector(`script[src="${src}"]`) !== null
}

/**
 * Load and execute a script from a URL. Use only on scripts you trust to avoid injection attacks!
 * @param url The URL of the script to load and execute.
 */
export async function loadAndExecuteScriptFromUrl(url: string): Promise<unknown> {
  if (isScriptLoaded(url)) {
    return
  }
  return await new Promise((resolve, reject) => {
    const script = document.createElement('script')
    script.onload = resolve
    script.onerror = reject
    script.async = true
    script.src = url
    document.head.appendChild(script)
  })
}

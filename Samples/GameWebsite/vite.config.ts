import { existsSync } from 'node:fs'
import { resolve, join } from 'node:path'
import { fileURLToPath, URL } from 'node:url'
import serveStatic from 'serve-static'
import { defineConfig, Plugin, loadEnv } from 'vite'

import vue, { Api } from '@vitejs/plugin-vue'

function serveStaticFiles(path: string, srcDir: string): Plugin<Api> {
  return {
    name: 'serve-static-files',
    configureServer(server): void {
      server.middlewares.use(
        path,
        // eslint-disable-next-line @typescript-eslint/strict-void-return -- looks like a type mismatch
        serveStatic(srcDir, {
          index: false,
          fallthrough: false,

          setHeaders: (res, path) => {
            // set content headers for brotli files (unity build)
            if (path.endsWith('.br')) {
              res.setHeader('Content-Encoding', 'br')

              if (path.endsWith('.js.br')) {
                res.setHeader('Content-Type', 'application/javascript')
              } else if (path.endsWith('.wasm.br')) {
                res.setHeader('Content-Type', 'application/wasm')
              } else {
                res.setHeader('Content-Type', 'binary/octet-stream')
              }
            }
          },
        })
      )
    },
  }
}

function tryGetUnityBuildOutputFile(buildRootPath: string, basePath: string): string {
  const candidate = join(buildRootPath, basePath)
  if (existsSync(candidate)) return basePath
  if (existsSync(`${candidate}.br`)) return `${basePath}.br`
  throw Error(`File not found in Unity build: ${basePath}`)
}

function getUnityBuildProps(unityBuildPath: string): {
  loaderUrl: string
  dataUrl: string
  frameworkUrl: string
  codeUrl: string
  playerWidth: number
  playerHeight: number
  companyName: string
  productName: string
  productVersion: string
} {
  // Unity uses base directory name as filename base by default
  const pathParts = unityBuildPath.split('/').filter((e) => e !== '')
  const baseName = pathParts[pathParts.length - 1]
  const rootDir = resolve(__dirname, unityBuildPath)
  return {
    loaderUrl: tryGetUnityBuildOutputFile(rootDir, `Build/${baseName}.loader.js`),
    dataUrl: tryGetUnityBuildOutputFile(rootDir, `Build/${baseName}.data`),
    frameworkUrl: tryGetUnityBuildOutputFile(rootDir, `Build/${baseName}.framework.js`),
    codeUrl: tryGetUnityBuildOutputFile(rootDir, `Build/${baseName}.wasm`),
    playerWidth: 960,
    playerHeight: 600,
    companyName: 'Metaplay',
    productName: 'Sample',
    productVersion: '1.0',
  }
}

// https://vitejs.dev/config/
export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '')
  const baseConfig = {
    base: '/',
    plugins: [vue()],
    resolve: {
      alias: {
        '@': fileURLToPath(new URL('./src', import.meta.url)),
      },
    },
    server: {
      host: true, // Allow connections from outside the local machine. Good for docker reverse proxy.
    },
    define: {},
  }

  if (env.VITE_UNITY_BUILD_PATH?.length) {
    baseConfig.plugins.push(serveStaticFiles(`/${env.VITE_WEBGL_BUILD_BASE_URL}`, env.VITE_UNITY_BUILD_PATH))
    const buildProps = getUnityBuildProps(env.VITE_UNITY_BUILD_PATH)
    if (buildProps) {
      baseConfig.define = Object.assign(baseConfig.define, {
        __UNITY_BUILD_PROPS__: JSON.stringify(buildProps),
      })
    }
  }

  return baseConfig
})

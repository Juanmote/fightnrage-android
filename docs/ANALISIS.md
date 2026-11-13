# Fight'N Rage → Android: análisis técnico

Estado a 2026-11-13 (actualizado: backend funcionando en PC, APK compilando en CI). Analizado sobre la versión GOG `v220120` (`FIGHT_N_RAGE.exe`, 2,5 MB).

## Conclusión

El juego es un ensamblado .NET Framework 4.0 (C#), **sin ofuscar**, con un motor propio
(`SebaGames.Framework`) que separa el juego de la plataforma mediante interfaces.
La versión de PC implementa esas interfaces con OpenTK 3.1 + FMOD 1.7.4.

**Estrategia:** no descompilar ni redistribuir el juego. La APK contiene solo un backend
nuevo para Android (SDL2 + OpenGL ES + audio) y un launcher. El usuario aporta su
`FIGHT_N_RAGE.exe` + carpeta `data` (GOG o Steam); el launcher carga el exe como
ensamblado y lo arranca con nuestro backend.

## Punto de entrada original

```csharp
// OpenTK_Implementation.Program.Main
var desc = SebaGames.Framework.AppDescriptor.Default;
new OpenTK_Implementation.OTKCore(null /*InitConfig*/).Initialize(desc);
```

Nuestro launcher hará lo mismo con `new AndroidCore().Initialize(AppDescriptor.Default)`.

## Interfaces a implementar (namespace `SebaGames.Framework`)

| Interfaz | Miembros | Implementación PC |
|---|---|---|
| ICoreEngine | 29 (Initialize, Close, Display/Input/SoundEngine, SystemInfo, eventos OnStart/OnClose/OnUpdate/OnRender/OnError/OnLosesFocus/OnRegainFocus/OnPassedOneSecond, informes FPS, TargetFrequency) | OTKCore |
| IDisplayEngine | 6 (MainSurface, CreateSurface, CreateBitmap×2, IsShaderAvailable, CreateShader) | OTKDisplayEngine |
| IDrawableSurface | 25 (Width/Height, Clear, matrices, Color, BlendMode, Smoothing, Shader, Draw, DrawVertexs×2, GetBitmap, ClipRect, Renderize…) | OTKFrameBuffer |
| IBitmap | 10 (Get/SetPixel(s), Save…) | OTKTexture |
| IShader | 6 (SetParameter×4, SetTexture) | OTKShader |
| IInputEngine | 8 (GetJoyPadStates, GetMouseState, eventos KeyPressed/KeyReleased/WriteChar) | OTKInputEngine |
| IAudioEngine / ISound / ISoundInstance / IMusic / IAudioPlayback | ~31 | FModAudioImplementation.FMOD_AudioEngine |

El propio exe trae `Empty_AudioImplementation.Empty_AudioEngine` (audio mudo): útil para la fase 1.

## Bucle principal (OTKCore.BaseInitialization)

- Ventana de `AppDescriptor.DisplayWidth × DisplayHeight`; superficie principal `MainSurfaceWidth × MainSurfaceHeight`.
- Frecuencia: `ForceFrequency` o refresco del monitor si Vsync (mínimo 60), o `MaxFps`; tope 500, por defecto 60. `mIdealDelta = 1/freq`.
- Orden: Input.Initialize → Display.Initialize(mainW, mainH) → Audio.Initialize → crea backbuffer → rellena `App.SystemInfo` → `OnStart()` → bucle (UpdateFrame/RenderFrame) → Dispose de display, audio y ventana.

## Dependencias de Windows

- `System.Windows.Forms`: solo en `GogWindowsLauncher.Program`, `OTKCore` e `Info` (MessageBox, Clipboard, Cursor, Screen). No las usamos.
- `System.Drawing`: Size/Rectangle/Color (existen en .NET moderno) y Bitmap/Icon solo en el backend de PC.
- `GalaxyCSharp` (GOG): `GogWindowsLauncher.Program` y `StatsListener`. En Steam será Steamworks. Habrá que neutralizarlo (stub o parche en el dispositivo con Mono.Cecil).
- Datos: `data/*.dat`, comprimidos con GZip; los lee el propio juego.
- Mandos: el PC usa `gamecontrollerdb.txt` (formato SDL2).

## Hallazgos de la implementación

- **Marca de 32 bits**: el exe es PE32 con `32BITREQUIRED`; los runtimes de 64 bits se niegan a cargarlo.
  `GameLauncher.PrepareExe` hace una copia quitando esa marca (equivale a `corflags /32BIT-`); nada más cambia.
- Cargado como ensamblado en .NET moderno, el juego lee `data/` sin problemas (`PathIndexer.SetFromDatFile`
  comprueba el tamaño de cada `.dat`; las claves usan `/`).
- **Sin referencia al exe en compilación**: `FnrStub` declara a mano las firmas que usa el backend con el mismo
  nombre de ensamblado (`FIGHT_N_RAGE`). En ejecución se resuelve al exe del usuario (`AssemblyResolve`).
- **Render**: el original usa GL de escritorio en modo inmediato (glBegin/glEnd, matrices fijas). El port
  transforma vértices en CPU y agrupa en lotes, vaciando el lote en cada cambio de estado (mismo resultado).
  Las superficies FBO guardan la imagen con la y del juego hacia arriba; solo la pantalla final invierte.
- **Shaders**: 42 shaders de fragmento GLSL 1.10 con `gl_TexCoord[0]`/`gl_Color`. Se convierten a GLSL ES 1.00
  sustituyendo esas variables por *varyings* propios; `custom.frag` usa inicializadores de uniform (no válidos
  en ES): se quitan y se aplican tras enlazar. Los 42 validan con glslangValidator.
- **Resolución interna** = 354×200 × `Constants.WINDOW_SCALE` (el PC la calcula según el monitor).
- **Mando**: replica `SDL_InputExtension.InputOverrideMethod` (analógicos como cruceta y stick izquierdo
  sumado a la cruceta, configuración por defecto de GOG).
- **Partidas**: mismo formato que PC (XML comprimido con gzip): se pueden importar/exportar.
- **Android**: `net8.0-android` ya no tiene soporte en el SDK actual → `net10.0-android`. El exe pide
  `mscorlib`/`System`/`System.Core`/`System.Xml` (fachadas de .NET Framework): el workflow las copia del
  runtime de Android como assets y se cargan bajo demanda; `Anchor` obliga a empaquetar los ensamblados reales.
- Ensamblados que carga una partida: CoreLib, Collections, Concurrent, IO.Compression, Linq, Private.Xml,
  Xml.ReaderWriter, RegularExpressions, Private.Uri, Threading, Memory, Text.Encoding.Extensions, Console.

## Plan

1. **Fase 1:** launcher + AndroidCore + gráficos (GLES 3) + mando, audio mudo. ✅ (falta probar en la consola)
2. **Fase 2:** audio (FMOD 2.x Android u otro motor).
3. **Fase 3:** controles táctiles, ajustes, soporte de la versión Steam.

## Pendiente

- Comprobar el exe de Steam.
- Probar la APK en la Odin 3 (log en `Android/data/com.juanmote.fightnrage/files/log.txt`).

using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("AnimeCS.PlayTests")]
[assembly: InternalsVisibleTo("AnimeCS.Editor")]
[assembly: InternalsVisibleTo("AnimeCS.EditorTests")]

#if ANIMECS_RENDER_PROBE
[assembly: InternalsVisibleTo("AnimeCS.RenderProbe")]
#endif

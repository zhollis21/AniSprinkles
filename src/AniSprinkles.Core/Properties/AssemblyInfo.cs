using System.Runtime.CompilerServices;

// The unit tests used to be link-compiled into one assembly with these sources, so they could reach
// deliberately-internal members such as MediaListSection.AllItems / BeginBulkUpdate. Now that they
// ProjectReference Core instead (#62), keep that reach rather than widening the members to public —
// they are implementation details the merger drives, not part of Core's surface.
[assembly: InternalsVisibleTo("AniSprinkles.UnitTests")]

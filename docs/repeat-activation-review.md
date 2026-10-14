# Repeat activation continuation

Run 38469262946 on 2b41a4d failed the repeat activation and distant-authoring
browser scenarios. The builds and six other shards succeeded; the run is not
accepted. The repeat failure observed False after actual native double-click
input, while snapshots continued to advance. Its earlier guards depended on
Click being nested inside a derived pointer dispatch override. Suppression now
belongs to the paired toggle/click activation and is consumed once; a new
press or keyboard event starts a fresh activation. No timeout or event replay
is added. The control exposes a bounded sixteen-stage native input trace, and
the existing browser regression retains that trace before its assertions.

The shipped assembly identifies Uno 6.7.135 at da217df2312e9e39676880f9cb4b8d519cc297e0.
This is different from Uno main; source/API review must use the shipped revision.
Its ButtonBase.OnClick is private protected, not a public override point.

The distant-authoring trace records only 25.4 ms for its last CPU recording,
with 128772 visible grid points in 32 DrawPoints submissions, yet later browser
scalar reads take tens of seconds. CPU recording time does not measure GPU
replay; this does not prove a complete root cause for the stall. All prior
pixel/input requirements and 72 scenarios remain mandatory before merging.

Primary references:
- https://github.com/unoplatform/uno/blob/da217df2312e9e39676880f9cb4b8d519cc297e0/src/Uno.UI/UI/Xaml/Controls/Primitives/ButtonBase/ButtonBase.mux.cs
- https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.uielement.doubletapped

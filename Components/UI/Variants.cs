// Mendix Tools — variant/size/tone enums for the UI primitives (MT-03).
// These mirror the string props of the design-system JSX references
// (docs/design-system/components/), renamed to C# conventions per the backlog.

namespace Mendix_Tools.Components.UI;

/// <summary>Button.jsx <c>variant</c>: primary | secondary | ghost | subtle | danger.</summary>
public enum ButtonVariant { Primary, Secondary, Ghost, Subtle, Danger }

/// <summary>IconButton.jsx <c>variant</c>: ghost | secondary | primary | danger.</summary>
public enum IconButtonVariant { Ghost, Secondary, Primary, Danger }

/// <summary>Control size (Button.jsx / IconButton.jsx <c>size</c>): sm (28px) | md (34px) | lg (40px).</summary>
public enum ControlSize { Sm, Md, Lg }

/// <summary>Badge.jsx <c>tone</c>.</summary>
public enum BadgeTone { Neutral, Accent, Success, Warning, Danger, Info, Db, Package }

/// <summary>Badge.jsx <c>size</c>: sm (18px) | md (22px).</summary>
public enum BadgeSize { Sm, Md }

/// <summary>Tag.jsx <c>tone</c>: neutral | accent.</summary>
public enum TagTone { Neutral, Accent }

/// <summary>Switch.jsx <c>size</c>: sm (32×18 track, 12px knob) | md (40×22 track, 16px knob).</summary>
public enum SwitchSize { Sm, Md }

// ---- MT-05 feedback primitives ----

/// <summary>Dialog.jsx <c>tone</c>: accent (default) | warning | danger. Drives the icon tile colour.</summary>
public enum DialogTone { Accent, Warning, Danger }

/// <summary>ProgressBar.jsx <c>tone</c>: accent | success | warning | danger | db | package.</summary>
public enum ProgressTone { Accent, Success, Warning, Danger, Db, Package }

/// <summary>ProgressBar.jsx <c>size</c>: sm (4px) | md (6px) | lg (10px).</summary>
public enum ProgressSize { Sm, Md, Lg }

/// <summary>Tooltip.jsx <c>placement</c>: top (default) | bottom | left | right.</summary>
public enum TooltipPlacement { Top, Bottom, Left, Right }

/// <summary>Toast tone (readme feedback description): neutral | accent | success | warning | danger | info.</summary>
public enum ToastTone { Neutral, Accent, Success, Warning, Danger, Info }

// ---- MT-06 data primitives ----

/// <summary>DataTable column alignment (DataTable.jsx column <c>align</c>): left (default) | center | right.</summary>
public enum ColumnAlign { Left, Center, Right }

/// <summary>LogViewer.jsx line <c>level</c> — drives the level tag and text colour.</summary>
public enum LogLevel { Info, Success, Warn, Error, Debug, Cmd }

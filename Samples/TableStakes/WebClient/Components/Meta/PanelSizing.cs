namespace WebClient.Components.Meta;

/// <summary>
/// Where on the screen a <see cref="ModalPanel"/> sits. <c>Centre</c> places it in the middle as a dialog.
/// <c>Bottom</c> anchors it to the bottom edge so the content above stays visible, for example the table.
/// </summary>
public enum PanelAnchor
{
    Centre,
    Bottom,
}

/// <summary>
/// How wide a <see cref="ModalPanel"/> is. <c>Narrow</c> is the matchmaking dialog width, <c>Standard</c> is the
/// results panel width, and <c>Wide</c> is the meta layer's content column width.
/// </summary>
public enum PanelWidth
{
    Narrow,
    Standard,
    Wide,
}

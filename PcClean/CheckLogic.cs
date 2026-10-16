namespace PcClean;

public static class CheckLogic
{
    public static void SetChecked(FileNode node, bool value)
    {
        SetDown(node, value);

        // Unchecking must also clear ancestors: otherwise a checked parent would
        // still cover (and delete) this unchecked node.
        if (!value)
            ClearAncestors(node);
    }

    static void SetDown(FileNode node, bool value)
    {
        node.Checked = value;
        foreach (var child in node.Children)
            SetDown(child, value);
    }

    static void ClearAncestors(FileNode node)
    {
        for (var parent = node.Parent; parent is not null; parent = parent.Parent)
            parent.Checked = false;
    }
}

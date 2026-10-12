using System.Windows.Controls;
using MockDataStudio.App.Samples;

namespace MockDataStudio.App.Controls;

// Aplica o estado inicial de expansão definido nos dados de exemplo (TreeNode.Expanded).
public static class TreeViewExtensions
{
    public static void ApplyInitialExpansion(this ItemsControl owner)
    {
        foreach (var item in owner.Items)
        {
            if (owner.ItemContainerGenerator.ContainerFromItem(item) is not TreeViewItem container)
            {
                continue;
            }

            if (item is TreeNode node)
            {
                container.IsExpanded = node.Expanded;
            }

            container.ApplyTemplate();
            container.UpdateLayout();
            container.ApplyInitialExpansion();
        }
    }
}

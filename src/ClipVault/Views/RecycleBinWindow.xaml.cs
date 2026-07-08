using System.Windows;
using ClipVault.Models;
using ClipVault.ViewModels;

namespace ClipVault.Views;

public partial class RecycleBinWindow : Window
{
    private readonly PopupViewModel _viewModel;

    public RecycleBinWindow(PopupViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    private RecycleBinEntry? SelectedEntry => RecycleList.SelectedItem as RecycleBinEntry;

    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        var entry = SelectedEntry;
        if (entry == null) return;
        _viewModel.RestoreItem(entry);
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        var entry = SelectedEntry;
        if (entry == null) return;

        var result = MessageBox.Show("永久删除后无法恢复，确定继续吗？", "永久删除",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result == MessageBoxResult.Yes)
            _viewModel.DeletePermanently(entry);
    }

    private void Empty_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.RecycleBinCount == 0) return;

        var result = MessageBox.Show("确定永久删除回收站中的全部记录吗？", "清空回收站",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result == MessageBoxResult.Yes)
            _viewModel.EmptyRecycleBin();
    }
}

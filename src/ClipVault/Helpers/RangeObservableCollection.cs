using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace ClipVault.Helpers;

/// <summary>
/// 支持 ReplaceAll 批量替换的 ObservableCollection，仅触发一次 Reset 通知。
/// 比逐条 Clear+Add（N+1 次通知）快 N 倍以上。
/// </summary>
public class RangeObservableCollection<T> : ObservableCollection<T>
{
    /// <summary>
    /// 批量替换所有元素，仅触发一次 Reset 通知
    /// </summary>
    public void ReplaceAll(IEnumerable<T> items)
    {
        CheckReentrancy();

        Items.Clear();
        foreach (var item in items)
            Items.Add(item);

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}

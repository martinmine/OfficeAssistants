using System.Collections.Specialized;
using System.ComponentModel;

namespace OfficeAssistant.Mac.Settings;

/// <summary>
/// A small stand-in for data binding: runs an update whenever a view model property or
/// collection changes, and once right away.
/// </summary>
internal sealed class ViewModelBinder
{
    private readonly Dictionary<string, List<Action>> _updates = [];

    public ViewModelBinder(INotifyPropertyChanged viewModel)
    {
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is { } name && _updates.TryGetValue(name, out var updates))
            {
                updates.ForEach(update => update());
            }
        };
    }

    /// <summary>Runs <paramref name="update"/> now and whenever any of <paramref name="properties"/> changes.</summary>
    public void On(Action update, params string[] properties)
    {
        foreach (var property in properties)
        {
            if (!_updates.TryGetValue(property, out var updates))
            {
                updates = [];
                _updates[property] = updates;
            }

            updates.Add(update);
        }

        update();
    }

    /// <summary>Runs <paramref name="update"/> now and whenever <paramref name="collection"/> changes.</summary>
    public static void OnCollection(INotifyCollectionChanged collection, Action update)
    {
        collection.CollectionChanged += (_, _) => update();
        update();
    }
}

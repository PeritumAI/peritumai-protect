using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;
using Peritum.Protect.Core;

namespace Peritum.Protect.App;

public sealed class TextSource : INotifyPropertyChanged
{
    public static TextSource Current { get; } = new();

    static TextSource() => TextCatalog.LanguageChanged += () =>
        Current.PropertyChanged?.Invoke(Current, new PropertyChangedEventArgs(Binding.IndexerName));

    public string this[string key] => TextCatalog.Get(key);

    public event PropertyChangedEventHandler? PropertyChanged;
}

public class LocExtension : MarkupExtension
{
    public LocExtension()
    {
    }

    public LocExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (serviceProvider.GetService(typeof(IProvideValueTarget)) is IProvideValueTarget target
            && target.TargetObject is DependencyObject)
        {
            return new Binding("[" + Key + "]")
            {
                Source = TextSource.Current,
                Mode = BindingMode.OneWay
            }.ProvideValue(serviceProvider);
        }

        return TextCatalog.Get(Key);
    }
}

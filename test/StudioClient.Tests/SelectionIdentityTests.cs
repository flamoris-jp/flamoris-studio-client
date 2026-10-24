using System.ComponentModel;
using Flamoris.StudioClient.App;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Flamoris.StudioClient.Tests;

[TestClass]
public class SelectionIdentityTests
{
    [TestMethod]
    public void HighlightChangesDoNotInvalidateSelectedRowLookup()
    {
        var row = new LyricRow(Guid.NewGuid(), 1, "lyrics", "--", "--");
        var selected = new Dictionary<LyricRow, int> { [row] = 1 }; var hash = row.GetHashCode();
        row.IsCurrent = true;
        Assert.AreEqual(hash, row.GetHashCode(), "A selected row must keep its dictionary key while highlighting changes.");
        Assert.IsTrue(selected.Remove(row)); Assert.HasCount(0, selected);
    }
    [TestMethod]
    public void BindingSubscriptionsDoNotInvalidateSelectedRowLookup()
    {
        var row = new LyricRow(Guid.NewGuid(), 1, "lyrics", "--", "--");
        var selected = new HashSet<LyricRow> { row }; var hash = row.GetHashCode();
        PropertyChangedEventHandler handler = (_, _) => { }; row.PropertyChanged += handler;
        Assert.AreEqual(hash, row.GetHashCode()); Assert.IsTrue(selected.Contains(row));
        row.PropertyChanged -= handler; Assert.IsTrue(selected.Remove(row));
    }
    [TestMethod]
    public void RowIdentityIsIndependentOfEqualDisplayedValues()
    {
        var id = Guid.NewGuid();
        var first = new LyricRow(id, 1, "same lyrics", "--", "--");
        var second = new LyricRow(id, 1, "same lyrics", "--", "--");
        Assert.AreNotEqual(first, second, "A newly replaced view row is a distinct presentation item.");
        var notifications = 0; first.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(first.IsCurrent)) notifications++; };
        first.IsCurrent = true; first.IsCurrent = true; first.IsCurrent = false;
        Assert.AreEqual(2, notifications);
    }
}

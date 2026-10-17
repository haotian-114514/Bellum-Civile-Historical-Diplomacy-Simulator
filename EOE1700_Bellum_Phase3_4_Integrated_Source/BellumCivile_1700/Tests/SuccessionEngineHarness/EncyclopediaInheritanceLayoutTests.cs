using System;
using System.Globalization;
using System.Reflection;
using System.Runtime.Serialization;
using System.Xml;
using BellumCivile;
using HarmonyLib;

internal static class EncyclopediaInheritanceLayoutTests
{
    internal static void Run(Action<bool, string> check)
    {
        var type = typeof(FeudalTitleRecord).Assembly.GetType(
            "BellumCivile.ViewModelMixin.EncyclopediaHeroFeudalTitlesPrefabExtension", true);
        var patch = Activator.CreateInstance(type, true);
        var document = (XmlDocument)AccessTools.Method(type, "GetPrefabExtension").Invoke(patch, null);
        var titles = (XmlElement)document.SelectSingleNode("//GridWidget[@DataSource='{BellumTitles}']");
        var inheritance = (XmlElement)document.SelectSingleNode("//GridWidget[@DataSource='{BellumInheritance}']");
        check(inheritance != null, "Inheritance uses a grid rather than a single-column list");
        check(inheritance.GetAttribute("ColumnCount") == "2"
            && inheritance.GetAttribute("ColumnCount") == titles.GetAttribute("ColumnCount"),
            "Inheritance matches the two-column Titles layout");
        check(inheritance.GetAttribute("DefaultCellWidth") == titles.GetAttribute("DefaultCellWidth")
            && inheritance.GetAttribute("WidthSizePolicy") == "StretchToParent",
            "Inheritance columns share the available page width like Titles");
        check(inheritance.GetAttribute("UseDynamicCellHeight") == "true"
            && inheritance.GetAttribute("HeightSizePolicy") == "CoverChildren",
            "Inheritance rows grow to accommodate wrapped names and provenance");

        var item = (XmlElement)inheritance.SelectSingleNode("ItemTemplate/ListPanel");
        check(item.GetAttribute("StackLayout.LayoutMethod") == "VerticalTopToBottom"
            && item.GetAttribute("HeightSizePolicy") == "CoverChildren",
            "Each title stays above its provenance within the same grid cell");
        check(item.GetAttribute("MarginRight") == "15" && item.GetAttribute("MarginBottom") == "12",
            "Inheritance entries retain column and row spacing");
        check(item.GetAttribute("WidthSizePolicy") == "Fixed"
            && float.Parse(item.GetAttribute("SuggestedWidth"), CultureInfo.InvariantCulture)
                + float.Parse(item.GetAttribute("MarginLeft"), CultureInfo.InvariantCulture)
                + float.Parse(item.GetAttribute("MarginRight"), CultureInfo.InvariantCulture)
                <= float.Parse(inheritance.GetAttribute("DefaultCellWidth"), CultureInfo.InvariantCulture),
            "Inheritance cells have a stable measurement width that fits the column");
        check(item.GetAttribute("VerticalAlignment") == "Top",
            "Short inheritance entries remain aligned with the top of their row");
        CheckNativePremeasurement(item, check);
        foreach (string binding in new[] { "@Definition", "@Value" })
        {
            var text = (XmlElement)item.SelectSingleNode("Children/TextWidget[@Text='" + binding + "']");
            check(text != null && text.GetAttribute("WidthSizePolicy") == "StretchToParent"
                && text.GetAttribute("HeightSizePolicy") == "CoverChildren",
                "Inheritance " + binding + " wraps within its column");
        }

        check(document.SelectSingleNode("//*[@Text='@BellumInheritanceCaveat']") == null,
            "Hero page no longer repeats the successor tooltip caveat");
        var divider = (XmlElement)document.SelectSingleNode("//*[@Id='BellumInheritanceDivider']");
        check(divider.GetAttribute("Parameter.ItemList") == "..\\BellumInheritanceParent"
            && inheritance.ParentNode.ParentNode.Attributes["Id"].Value == "BellumInheritanceParent",
            "Inheritance section collapse still targets the full list");
    }

    private static void CheckNativePremeasurement(XmlElement template, Action<bool, string> check)
    {
        // Exercise native sizing without a renderer: GridLayout measures dynamic cells with Vector2.Zero.
        var assembly = Assembly.Load("TaleWorlds.GauntletUI");
        AccessTools.Method(assembly.GetType("TaleWorlds.GauntletUI.WidgetInfo", true), "Refresh").Invoke(null, null);
        var contextType = assembly.GetType("TaleWorlds.GauntletUI.UIContext", true);
        var context = FormatterServices.GetUninitializedObject(contextType);
        var events = FormatterServices.GetUninitializedObject(assembly.GetType("TaleWorlds.GauntletUI.EventManager", true));
        AccessTools.Property(contextType, "EventManager").SetValue(context, events);
        var cellType = assembly.GetType("TaleWorlds.GauntletUI.BaseTypes.ListPanel", true);
        var cell = Activator.CreateInstance(cellType, context);
        foreach (string name in new[] { "WidthSizePolicy", "HeightSizePolicy", "SuggestedWidth", "MarginLeft", "MarginRight" })
        {
            var property = AccessTools.Property(cellType, name);
            property.SetValue(cell, property.PropertyType.IsEnum
                ? Enum.Parse(property.PropertyType, template.GetAttribute(name))
                : Convert.ChangeType(template.GetAttribute(name), property.PropertyType, CultureInfo.InvariantCulture));
        }
        var measure = AccessTools.Method(cellType, "Measure");
        var vectorType = measure.GetParameters()[0].ParameterType;
        object zeroWidth = Activator.CreateInstance(vectorType);
        foreach (float scale in new[] { 0.75f, 1f, 1.5f })
        {
            AccessTools.Property(contextType, "CustomScale").SetValue(context, scale);
            AccessTools.Property(contextType, "Scale").SetValue(context, scale);
            measure.Invoke(cell, new[] { zeroWidth });
            var measured = AccessTools.Property(cellType, "MeasuredSize").GetValue(cell);
            float width = (float)vectorType.GetField("X").GetValue(measured);
            float expected = float.Parse(template.GetAttribute("SuggestedWidth"), CultureInfo.InvariantCulture) * scale;
            check(width > 0f && Math.Abs(width - expected) < 0.01f,
                "Native zero-width premeasurement preserves the inheritance text width at scale " + scale);
        }

        var widthPolicy = AccessTools.Property(cellType, "WidthSizePolicy");
        widthPolicy.SetValue(cell, Enum.Parse(widthPolicy.PropertyType, "StretchToParent"));
        measure.Invoke(cell, new[] { zeroWidth });
        var oldMeasurement = AccessTools.Property(cellType, "MeasuredSize").GetValue(cell);
        check((float)vectorType.GetField("X").GetValue(oldMeasurement) <= 0f,
            "Regression control reproduces the old stretch-width cell's invalid premeasurement");
    }
}

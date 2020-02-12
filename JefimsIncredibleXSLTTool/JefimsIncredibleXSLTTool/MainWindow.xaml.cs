﻿using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Folding;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using ICSharpCode.AvalonEdit.Search;
using JefimsIncredibleXsltTool.Lib;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Xml;
using System.Xml.Linq;
using ToastNotifications.Messages;

namespace JefimsIncredibleXsltTool
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        private readonly MainViewModel _mainViewModel;
        private CompletionWindow _completionWindow;
        private readonly XmlFoldingStrategy _strategy;
        private FoldingManager _sourceXsltFoldingManager;
        private readonly FoldingManager _sourceXmlFoldingManager;
        private readonly FoldingManager _outputXmlFoldingManager;

        public MainWindow()
        {
            _mainViewModel = new MainViewModel();
            DataContext = _mainViewModel;
            InitializeComponent();
            LoadOldSettings();
            _sourceXmlFoldingManager = FoldingManager.Install(SourceXml.TextArea);
            _sourceXsltFoldingManager = FoldingManager.Install(SourceXslt.TextArea);
            _outputXmlFoldingManager = FoldingManager.Install(OutputXml.TextArea);

            SearchPanel.Install(SourceXml);
            SearchPanel.Install(OutputXml);
            SearchPanel.Install(SourceXslt);

            _strategy = new XmlFoldingStrategy();
            UpdateFolding();

            _mainViewModel.OnTransformFinished += delegate { UpdateFolding(); };

            SourceXslt.TextArea.TextEntering += TextEditor_TextArea_TextEntering;
            SourceXslt.TextArea.TextEntered += TextEditor_TextArea_TextEntered;
            SourceXml.TextArea.TextEntered += (a, b) =>
            {
                try
                {
                    var xpath = GetXmlXPath(true);
                    TextBlockXPath.Text = xpath;
                }
                catch (Exception)
                {
                    TextBlockXPath.Text = "Error getting XPath";
                }
            };
        }
        
        private void TextEditor_TextArea_TextEntered(object sender, TextCompositionEventArgs e)
        {
            if (e.Text != "<") return;

            _completionWindow = new CompletionWindow(SourceXslt.TextArea);
            var data = _completionWindow.CompletionList.CompletionData;
            data.Add(new XmlCompletionData("xsl:choose", "xsl:choose></xsl:choose>", "Provides multiple conditional testing in conjunction with the <xsl:otherwise> element and <xsl:when> element."));
            data.Add(new XmlCompletionData("xsl:for-each", "xsl:for-each select=\"temp\"></xsl:for-each>", "Applies a template repeatedly, applying it in turn to each node in a set."));
            data.Add(new XmlCompletionData("xsl:if", "xsl:if test=\"temp\"></xsl:if>", "Allows simple conditional template fragments."));
            data.Add(new XmlCompletionData("xsl:otherwise", "xsl:otherwise></xsl:otherwise>", "Provides multiple conditional testing in conjunction with the <xsl:choose> element and <xsl:when> element."));
            data.Add(new XmlCompletionData("xsl:stylesheet", "xsl:stylesheet version=\"2.0\"\r\n  xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\"\r\n  xmlns:xs=\"http://www.w3.org/2001/XMLSchema\"\r\n  xmlns:fn=\"http://www.w3.org/2005/xpath-functions\"\r\n  exclude-result-prefixes=\"xs fn\">\r\n  <xsl:output version=\"1.0\" method=\"xml\" omit-xml-declaration=\"yes\" indent=\"yes\" />\r\n\r\n  <xsl:template match=\"/\">\r\n    <xsl:text>temp</xsl:text>\r\n  </xsl:template>\r\n</xsl:stylesheet>", "Template stylesheet."));
            data.Add(new XmlCompletionData("xsl:text", "xsl:text></xsl:text>", "Generates text in the output."));
            data.Add(new XmlCompletionData("xsl:value-of", "xsl:value-of select=\"temp\" />", "Inserts the value of the selected node as text."));
            data.Add(new XmlCompletionData("xsl:variable", "xsl:variable name=\"temp\" select=\"temp\" />", "Specifies a value bound in an expression."));
            data.Add(new XmlCompletionData("xsl:when", "xsl:when test=\"temp\"></xsl:when>", "Provides multiple conditional testing in conjunction with the <xsl:choose> element and <xsl:otherwise> element."));

            int? offset = 0;
            _completionWindow.Show();
            _completionWindow.Closed += delegate
            {
                if (_completionWindow?.CompletionList.SelectedItem != null)
                    offset = _completionWindow.CompletionList.SelectedItem.Text.Length - _completionWindow.CompletionList.SelectedItem.Text.IndexOf('>') - 1;
                _completionWindow = null;
            };

            _completionWindow.CompletionList.InsertionRequested += delegate
            {
                if (offset != null)
                    SourceXslt.CaretOffset = SourceXslt.CaretOffset - (int)offset;
            };
        }

        private void ChangeColorTheme(ColorTheme colorTheme)
        {
            _mainViewModel.ColorTheme = colorTheme;
            using (Stream s = GetType().Assembly.GetManifestResourceStream(colorTheme.AvalonXmlHighlightResourceName))
            {
                using (XmlTextReader reader = new XmlTextReader(s))
                {
                    var highlightingDef = HighlightingLoader.Load(reader, HighlightingManager.Instance);
                    SourceXslt.SyntaxHighlighting = highlightingDef;
                    SourceXml.SyntaxHighlighting = highlightingDef;
                    OutputXml.SyntaxHighlighting = highlightingDef;
                    Errors.SyntaxHighlighting = highlightingDef;
                }
            }

            Properties.Settings.Default.theme = colorTheme.Id;
            Properties.Settings.Default.Save();
        }

        private void HightlightSyntax(string embeddedResourceName)
        {
            using (Stream s = GetType().Assembly.GetManifestResourceStream(embeddedResourceName))
            {
                using (XmlTextReader reader = new XmlTextReader(s))
                {
                    var highlightingDef = HighlightingLoader.Load(reader, HighlightingManager.Instance);
                    SourceXslt.SyntaxHighlighting = highlightingDef;
                    SourceXml.SyntaxHighlighting = highlightingDef;
                    OutputXml.SyntaxHighlighting = highlightingDef;
                    Errors.SyntaxHighlighting = highlightingDef;
                }
            }
        }

        private void TextEditor_TextArea_TextEntering(object sender, TextCompositionEventArgs e)
        {
            if (e.Text.Length <= 0) return;

            if (_completionWindow != null && (e.Text[0] == '>' || e.Text[0] == '/'))
            {
                _completionWindow?.Close();
            }

            try
            {
                if (e.Text[0] == '/')
                {
                    var textArea = e.Source as TextArea;
                    var xml = textArea.Document.Text;
                    var cursorPosition = textArea.Caret.Offset;
                    if (cursorPosition == 0) return;
                    var prevSymbol = xml[cursorPosition - 1];
                    if (prevSymbol != '<') return;
                    var closer = new TagCloser();
                    var autocompleteTag = closer.GetClosingTagIfAny(xml, cursorPosition);
                    Console.WriteLine($"Autocomplete tag: {autocompleteTag}");
                    if (!string.IsNullOrWhiteSpace(autocompleteTag))
                    {
                        textArea.Document.Insert(textArea.Caret.Offset, $"/{autocompleteTag}>");
                        e.Handled = true;
                    }
                }
            }
            catch(Exception ex)
            {
                // We do not want to be disrupting anyone with crashes in this
                // autocomplete. Also, I do not know when I am going to come back
                // to this code to fix any bugs that can be here.
                Console.WriteLine(ex.ToString());
            }
        }

        public void UpdateFolding()
        {
            try
            {
                _strategy.UpdateFoldings(_sourceXsltFoldingManager, SourceXslt.Document);
                _strategy.UpdateFoldings(_sourceXmlFoldingManager, SourceXml.Document);
                _strategy.UpdateFoldings(_outputXmlFoldingManager, OutputXml.Document);
            }
            catch
            {
                Console.WriteLine(@"Folding error");
            }
        }

        private void NewCommand_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            FoldingManager.Uninstall(_sourceXsltFoldingManager);
            _mainViewModel.New();
            _sourceXsltFoldingManager = FoldingManager.Install(SourceXslt.TextArea);
            UpdateFolding();
        }

        private void SaveCommand_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            _mainViewModel.Save();
        }

        private void OpenCommand_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            var ofd = new OpenFileDialog { Filter = "Supported file types|*.xsl;*.xslt|All files|*.*" };
            if (ofd.ShowDialog() != true) return;
            try
            {
                FoldingManager.Uninstall(_sourceXsltFoldingManager);
                _mainViewModel.OpenFile(ofd.FileName);
                _sourceXsltFoldingManager = FoldingManager.Install(SourceXslt.TextArea);
                UpdateFolding();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_mainViewModel.XmlToTransformDocument != null)
            {
                Properties.Settings.Default.xmlContent = _mainViewModel.XmlToTransformDocument.Text;
            }
            if (_mainViewModel.Document != null)
            {
                Properties.Settings.Default.xsltPath = _mainViewModel.Document.FilePath;
                Properties.Settings.Default.xsltContentIfPathEmpty = _mainViewModel.Document.TextDocument.Text;
            }

            if (_mainViewModel.ValidationSchemaFile != null)
            {
                Properties.Settings.Default.xsdPath = _mainViewModel.ValidationSchemaFile;
            }

            Properties.Settings.Default.Save();
        }

        private void LoadOldSettings()
        {
            try
            {
                if (Properties.Settings.Default.xsltPath != null && File.Exists(Properties.Settings.Default.xsltPath))
                {
                    _mainViewModel.OpenFile(Properties.Settings.Default.xsltPath);
                }
                else if (Properties.Settings.Default.xsltContentIfPathEmpty != null)
                {
                    _mainViewModel.Document.TextDocument.Text = Properties.Settings.Default.xsltContentIfPathEmpty;
                }

                if (Properties.Settings.Default.xmlContent != null)
                {
                    _mainViewModel.XmlToTransformDocument.Text = Properties.Settings.Default.xmlContent;
                }
                if (Properties.Settings.Default.xsdPath != null && File.Exists(Properties.Settings.Default.xsdPath))
                {
                    _mainViewModel.ValidationSchemaFile = Properties.Settings.Default.xsdPath;
                }

                var theme = ColorTheme.ColorThemes.FirstOrDefault(t => t.Id == Properties.Settings.Default.theme);
                theme = theme ?? ColorTheme.DefaultColorTheme;
                ChangeColorTheme(theme);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
            }
        }

        private string GetXmlXPath(bool includeIndexes)
        {
            var xel = XPathHelpers.GetXElementFromCursor(SourceXml.Document.Text, SourceXml.TextArea.Caret.Position.Line, SourceXml.TextArea.Caret.Position.Column);
            var xpath = XPathHelpers.GetAbsoluteXPath(xel, includeIndexes);
            return xpath;
        }

        private void MenuItemCopyXPath_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var xpath = GetXmlXPath(true);
                Clipboard.SetText(xpath);
                _mainViewModel.Notifier.ShowSuccess("XPath copied. \\o/");
            }
            catch (Exception ex)
            {
                _mainViewModel.Notifier.ShowError("Failed to copy XPath: " + ex.Message);
            }
        }

        private void MenuItemCopyXPathNoIndexes_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var xpath = GetXmlXPath(false);
                Clipboard.SetText(xpath);
                _mainViewModel.Notifier.ShowSuccess("XPath copied. Yay!");
            }
            catch (Exception ex)
            {
                _mainViewModel.Notifier.ShowError("Failed to copy XPath: " + ex.Message);
            }
        }

        private void SourceXml_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var textEditor = sender as TextEditor;
            var position = textEditor?.GetPositionFromPoint(e.GetPosition(textEditor));
            if (position.HasValue)
            {
                textEditor.TextArea.Caret.Position = position.Value;
            }
        }

        private void ButtonCopyXPathNoIndexes_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var xpath = GetXmlXPath(false);
                Clipboard.SetText(xpath);
                _mainViewModel.Notifier.ShowSuccess("XPath copied. Wohoo!");
            }
            catch (Exception ex)
            {
                _mainViewModel.Notifier.ShowError("Failed to copy XPath: " + ex.Message);
            }
        }

        private void ButtonCopyXPath_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var xpath = GetXmlXPath(true);
                Clipboard.SetText(xpath);
                _mainViewModel.Notifier.ShowSuccess("XPath copied. \\o/");
            }
            catch (Exception ex)
            {
                _mainViewModel.Notifier.ShowError("Failed to copy XPath: " + ex.Message);
            }
        }

        private void ButtonPrettyPrint_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SourceXml.Document.Text = PrettyXml(SourceXml.Document.Text);
                _mainViewModel.Notifier.ShowSuccess("Uuuhh, so pretty!");
            }
            catch (Exception ex)
            {
                _mainViewModel.Notifier.ShowError("Failed to pretty print: " + ex.Message);
            }

            try
            {
                SourceXslt.Document.Text = PrettyXml(SourceXslt.Document.Text);
                _mainViewModel.Notifier.ShowSuccess("It's beautiful!");
            }
            catch (Exception ex)
            {
                _mainViewModel.Notifier.ShowError("Failed to pretty print: " + ex.Message);
            }
        }


        private static string PrettyXml(string xml)
        {
            return XDocument.Parse(xml).ToString();
        }

        private void SourceXml_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            try
            {
                var xpath = GetXmlXPath(true);
                TextBlockXPath.Text = xpath;
            }
            catch (Exception)
            {
                TextBlockXPath.Text = "Error getting XPath";
            }
        }

        private void ChangeColorTheme_MouseUp(object sender, MouseButtonEventArgs e)
        {
            var el = sender as FrameworkElement;
            var colorTheme = el.DataContext as ColorTheme;
            if (colorTheme != null) ChangeColorTheme(colorTheme);
        }
    }
}

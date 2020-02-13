using ICSharpCode.AvalonEdit.Document;
using JefimsIncredibleXsltTool.Lib;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using System.Xml.Xsl;
using ToastNotifications;
using ToastNotifications.Lifetime;
using ToastNotifications.Position;
using ToastNotifications.Messages;

namespace JefimsIncredibleXsltTool
{
    public class Observable : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            var evt = PropertyChanged;
            evt?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class MainViewModel : Observable
    {
        public Notifier Notifier = new Notifier(cfg =>
        {
            cfg.DisplayOptions.TopMost = false;
            cfg.PositionProvider = new WindowPositionProvider(
                parentWindow: Application.Current.MainWindow,
                corner: Corner.BottomRight,
                offsetX: 10,
                offsetY: 10);

            cfg.LifetimeSupervisor = new TimeAndCountBasedLifetimeSupervisor(
                notificationLifetime: TimeSpan.FromSeconds(2),
                maximumNotificationCount: MaximumNotificationCount.FromCount(5));

            cfg.Dispatcher = Application.Current.Dispatcher;
        });

        private Document _document;
        private const string ProgramName = "XSLT 转换工具";
        public event EventHandler OnTransformFinished;
        public ColorTheme ColorTheme
        {
            get => _colorTheme;
            set
            {
                _colorTheme = value;
                OnPropertyChanged("ColorTheme");
            }
        }
        public MainViewModel()
        {
            SetupTimer();
            XsltParameters = new ObservableCollection<XsltParameter>();
            XsltParameters.CollectionChanged += (a, b) => RunTransform();
            Document = new Document();
            XmlToTransformDocument.TextChanged += (a, b) => RunTransform();
        }

        public string WindowTitle => Document == null ? ProgramName : $"{Document.Display} - {ProgramName}";

        public Document Document
        {
            get => _document;
            private set
            {
                _document = value;
                if (_document != null)
                {
                    _document.TextDocument.TextChanged += TextDocument_TextChanged;
                }

                OnPropertyChanged("Document");
                OnPropertyChanged("WindowTitle");
            }
        }

        private void TextDocument_TextChanged(object sender, EventArgs e)
        {
            RunTransform();
            OnPropertyChanged("WindowTitle");
        }

        public TextDocument XmlToTransformDocument { get; } = new TextDocument();

        public TextDocument ResultingXmlDocument { get; } = new TextDocument();

        public TextDocument ErrorsDocument { get; } = new TextDocument();

        public bool ErrorsExist => ErrorsDocument.Text.Length > 0;

        protected void TransformFinished()
        {
            var evt = OnTransformFinished;
            evt?.Invoke(this, EventArgs.Empty);
        }

        public ObservableCollection<XsltParameter> XsltParameters { get; }

        internal void OpenFile(string fileName)
        {
            Document = new Document(fileName);
            var paramNames = ExtractParamsFromXslt(Document.TextDocument.Text);
            XsltParameters.Clear();
            paramNames.ToList().ForEach((o) => XsltParameters.Add(new XsltParameter { Name = o }));
        }

        internal void New()
        {
            if (Document != null && Document.IsModified)
            {
                var answer = MessageBox.Show("You have unsaved changes in current document. Discard?", "Warning", MessageBoxButton.OKCancel);
                if (answer != MessageBoxResult.OK) return;
            }

            Document = new Document();
        }

        internal void Save()
        {
            if (Document == null)
            {
                Notifier.ShowWarning("No open file. This should not have happened :( Apologies.");
                return;
            }

            if (Document.IsNew)
            {
                var ofd = new SaveFileDialog
                {
                    Filter = "XSLT|*.xslt|All files|*.*",
                    RestoreDirectory = true,
                    Title = "Save new file as..."
                };
                if (ofd.ShowDialog() == true)
                {
                    Document.FilePath = ofd.FileName;
                }
            }

            try
            {
                if (Document.Save())
                    Notifier.ShowSuccess("Saved! ☃");
            }
            catch (Exception ex)
            {
                Notifier.ShowError(ex.ToString());
            }
        }
        public List<ColorTheme> ColorThemes { get { return ColorTheme.ColorThemes; } }

        private static IEnumerable<string> ExtractParamsFromXslt(string xslt)
        {
            try
            {
                var doc = new XmlDocument();
                doc.LoadXml(xslt);
                XmlNode root = doc.DocumentElement;

                var nodes = root?.SelectNodes("//*[local-name()='param']/@name");
                var result = new List<string>();
                if (nodes == null) return result;
                foreach (var node in nodes)
                {
                    result.Add(((XmlAttribute)node).Value);
                }

                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                return new string[0];
            }
        }

        private Timer _runTransformTimer;

        private void SetupTimer()
        {
            _runTransformTimer = new Timer(200) { Enabled = false, AutoReset = false };
            _runTransformTimer.Elapsed += (sender, args) => Application.Current.Dispatcher.Invoke(RunTransformImpl);
        }

        public void RunTransform()
        {
            _runTransformTimer.Stop();
            _runTransformTimer.Start();
        }

        public bool RunTransformImpl()
        {
            if (XmlToTransformDocument == null || string.IsNullOrWhiteSpace(Document?.TextDocument?.Text))
                return false;

            var xml = XmlToTransformDocument.Text;
            var xslt = Document.TextDocument.Text;
            Task.Factory.StartNew(() =>
            {
                try
                {
                    string result = null;
                    result = XsltTransformDotNet(xml, xslt, XsltParameters.Where(o => o?.Name != null).ToArray());

                    var validation = Validate(result);
                    if (validation != null)
                    {
                        Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            ResultingXmlDocument.Text = result;
                            ErrorsDocument.Text = validation;
                        }));
                    }
                    else
                    {
                        Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            ResultingXmlDocument.Text = result;
                            ErrorsDocument.Text = string.Empty;
                        }));
                    }
                }
                catch (Exception ex)
                {
                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        ErrorsDocument.Text = ex.InnerException?.ToString() ?? ex.Message;
                    }));
                }
                finally
                {
                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        OnPropertyChanged("ErrorsExist");
                        TransformFinished();
                    }));
                }
            });

            return true;
        }

        private string Validate(string xml)
        {
            if (string.IsNullOrWhiteSpace(ValidationSchemaFile)) return null;
            if (string.IsNullOrWhiteSpace(xml)) return null;
            var schemas = new XmlSchemaSet();
            var schema = XmlSchema.Read(new XmlTextReader(ValidationSchemaFile), null);
            schemas.Add(schema);

            var doc = XDocument.Parse(xml);

            string message = null;
            doc.Validate(schemas, (o, e) =>
            {
                message = e.Message;
            });

            return message;
        }

        private string _validationSchemaFile;
        private ColorTheme _colorTheme = new ColorTheme();

        public string ValidationSchemaFile
        {
            get => _validationSchemaFile;
            set
            {
                _validationSchemaFile = value;
                OnPropertyChanged("ValidationSchemaFile");
            }
        }

        public static string XsltTransformDotNet(string xmlString, string xslt, XsltParameter[] xsltParameters)
        {
            using (var xmlDocumenOut = new StringWriter())
            using (StringReader xmlReader = new StringReader(xmlString), xsltReader = new StringReader(xslt))
            using (XmlReader xmlDocument = XmlReader.Create(xmlReader), xsltDocument = XmlReader.Create(xsltReader))
            {
                var xsltSettings = new XsltSettings(true, true);
                var myXslTransform = new XslCompiledTransform();
                myXslTransform.Load(xsltDocument, xsltSettings, new XmlUrlResolver());
                var argsList = new XsltArgumentList();
                xsltParameters?.ToList().ForEach(x => argsList.AddParam(x.Name, "", x.Value));
                using (var xmlTextWriter = XmlWriter.Create(xmlDocumenOut, myXslTransform.OutputSettings))
                {
                    myXslTransform.Transform(xmlDocument, argsList, xmlTextWriter);
                    var result = xmlDocumenOut.ToString().Trim('\uFEFF');
                    try
                    {
                        return XDocument.Parse(result).ToString();
                    }
                    catch (Exception)
                    {
                        return result;
                    }
                }
            }
        }
    }

    public class Document : Observable
    {
        private string _filePath;
        private string _originalContents;
        private TextDocument _textDocument;

        public Document()
        {
            IsNew = true;
            var contents = string.Empty;
            _originalContents = string.Empty;
            TextDocument = new TextDocument(new StringTextSource(contents));
        }

        public Document(string filePath)
        {
            IsNew = false;
            FilePath = filePath;
            var contents = File.ReadAllText(FilePath);
            _originalContents = contents;
            TextDocument = new TextDocument(new StringTextSource(contents));
            TextDocument.Changed += TextDocument_Changed;
        }

        private void TextDocument_Changed(object sender, DocumentChangeEventArgs e)
        {
            OnPropertyChanged("Display");
            OnPropertyChanged("IsModified");
        }

        public bool IsNew { get; private set; }

        public bool IsModified => TextDocument != null && TextDocument.Text != _originalContents;

        public TextDocument TextDocument
        {
            get => _textDocument;
            private set
            {
                _textDocument = value;
                OnPropertyChanged("TextDocument");
            }
        }

        public string Display
        {
            get
            {
                var result = IsNew ? "Unsaved document" : Path.GetFileName(FilePath);
                if (IsModified) result += " *";
                return result;
            }
        }

        public string FilePath
        {
            get => _filePath;
            set
            {
                _filePath = value;
                IsNew = false;
                OnPropertyChanged("FilePath");
                OnPropertyChanged("Display");
            }
        }

        internal bool Save()
        {
            if (FilePath == null)
            {
                return false;
            }
            File.WriteAllText(FilePath, TextDocument.Text);
            IsNew = false;
            _originalContents = TextDocument.Text;
            OnPropertyChanged("IsModified");
            OnPropertyChanged("Display");
            return true;
        }
    }
}

using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using PlatypusTools.Core.Models.Metadata;

namespace PlatypusTools.UI.Views
{
    public partial class MetadataTemplateEditorDialog : Window
    {
        private readonly MetadataTemplate _template;
        private readonly ObservableCollection<MetadataField> _fields;

        /// <summary>
        /// Whether the user saved changes (true) or cancelled (false).
        /// </summary>
        public bool Saved { get; private set; }

        public MetadataTemplateEditorDialog(MetadataTemplate template)
        {
            InitializeComponent();

            _template = template;

            // Populate UI from template
            TemplateNameBox.Text = template.Name;
            TemplateCategoryBox.Text = template.Category;
            TemplateDescriptionBox.Text = template.Description;

            // Clone fields so edits can be cancelled
            _fields = new ObservableCollection<MetadataField>(
                template.Fields.Select(f => f.Clone()));

            FieldsGrid.ItemsSource = _fields;

            Loaded += (s, e) => TemplateNameBox.Focus();
        }

        private void AddField_Click(object sender, RoutedEventArgs e)
        {
            _fields.Add(new MetadataField
            {
                Name = "NewTag",
                DisplayName = "New Tag",
                Value = string.Empty,
                Type = MetadataFieldType.Text,
                IsEnabled = true
            });

            // Scroll to and select the new field
            FieldsGrid.ScrollIntoView(_fields.Last());
            FieldsGrid.SelectedItem = _fields.Last();
        }

        private void RemoveField_Click(object sender, RoutedEventArgs e)
        {
            if (FieldsGrid.SelectedItem is MetadataField field)
            {
                _fields.Remove(field);
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TemplateNameBox.Text))
            {
                MessageBox.Show("Template name cannot be empty.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Apply changes to the template
            _template.Name = TemplateNameBox.Text.Trim();
            _template.Category = TemplateCategoryBox.Text.Trim();
            _template.Description = TemplateDescriptionBox.Text.Trim();
            _template.Fields = _fields.ToList();
            _template.ModifiedAt = System.DateTime.Now;

            Saved = true;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Saved = false;
            DialogResult = false;
            Close();
        }
    }
}

using System.Collections.ObjectModel;
using PlatypusTools.Core.Services;
using System.Linq;

namespace PlatypusTools.UI.ViewModels
{
    public class DuplicateGroupViewModel
    {
        public DuplicateGroupViewModel(DuplicateGroup g)
        {
            Hash = g.Hash;
            Files = new ObservableCollection<DuplicateFileViewModel>(g.Files.Select(f => new DuplicateFileViewModel(f)));
        }

        public string Hash { get; }
        public ObservableCollection<DuplicateFileViewModel> Files { get; }
    }
}
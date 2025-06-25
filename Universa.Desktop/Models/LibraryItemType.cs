namespace Universa.Desktop.Models
{
    public enum LibraryItemType
    {
        Unknown = 0,
        Directory = 1,
        File = 2,
        VirtualToDos = 3,  // For the aggregated ToDos view
        Service = 4,       // For media and chat services
        Category = 5,      // For grouping items like Services
        Error = 6,         // For error states
        Library = 7,
        MovieLibrary = 8,
        TVLibrary = 9,
        MusicLibrary = 10,
        Movie = 11,
        Series = 12,
        Season = 13,
        Episode = 14,
        Album = 15,
        Track = 16,
        Artist = 17,
        Playlist = 18,
        Folder = 19,
        Overview = 20,     // For the Overview tab (deprecated)
        Inbox = 21,       // For the Inbox feature
        GlobalAgenda = 22 // For the Global Agenda tab
    }
} 
using System.Globalization;

namespace MyStoryTold.Services;

/// <summary>
/// Site-chrome translator. Replaces ASP.NET Core's resx-based
/// IStringLocalizer pipeline because the resx assembly-resource lookup
/// was silently falling through to English keys regardless of the active
/// culture. An in-code dictionary keyed by the English source string is
/// trivial to debug, trivial to extend, and makes the cost of adding a
/// new language explicit (drop a new dictionary, that's it).
///
/// Use from a Razor view:
///     @inject MyStoryTold.Services.ILocalizer _t
///     @_t["Save"]
///
/// Missing keys fall back to the English literal (which is the key
/// itself) — same fallback semantics as IStringLocalizer.
/// </summary>
public interface ILocalizer
{
    string this[string key] { get; }
    string Culture { get; }
}

public class Localizer : ILocalizer
{
    public string this[string key]
    {
        get
        {
            if (string.IsNullOrEmpty(key)) return key ?? "";
            if (Culture == "it" && Translations.It.TryGetValue(key, out var v)) return v;
            return key;
        }
    }

    /// <summary>
    /// Reads the active culture from the standard ASP.NET Core
    /// localization pipeline, which `app.UseRequestLocalization` populates
    /// from the `.AspNetCore.Culture` cookie. Two-letter form ("en", "it")
    /// keeps the dictionary keys simple.
    /// </summary>
    public string Culture =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
}

internal static class Translations
{
    public static readonly Dictionary<string, string> It = new(StringComparer.Ordinal)
    {
        // Common buttons
        ["Save"] = "Salva",
        ["Save changes"] = "Salva modifiche",
        ["Cancel"] = "Annulla",
        ["Edit"] = "Modifica",
        ["Delete"] = "Elimina",
        ["Send"] = "Invia",
        ["Back"] = "Indietro",
        ["Continue"] = "Continua",
        ["Open"] = "Apri",
        ["Close"] = "Chiudi",
        ["Confirm"] = "Conferma",
        ["Submit"] = "Invia",
        ["Yes"] = "Sì",
        ["No"] = "No",
        ["Loading…"] = "Caricamento…",
        ["Read more"] = "Leggi di più",
        ["Read less"] = "Mostra meno",
        ["Try again"] = "Riprova",
        ["Apply now"] = "Applica ora",

        // Navigation / sections
        ["Home"] = "Home",
        ["My Stories"] = "Le mie storie",
        ["Channels"] = "Canali",
        ["Biographies"] = "Biografie",
        ["Friends"] = "Amici",
        ["Settings"] = "Impostazioni",
        ["Sign out"] = "Esci",
        ["Sign in"] = "Accedi",
        ["Sign In"] = "Accedi",
        ["Sign up"] = "Registrati",
        ["Get Started"] = "Inizia",
        ["Start Here"] = "Inizia qui",
        ["My Story"] = "La mia storia",
        ["Network"] = "Rete",
        ["Alerts"] = "Notifiche",
        ["Search"] = "Cerca",
        ["Messages"] = "Messaggi",
        ["Inbox"] = "Posta in arrivo",
        ["Profile"] = "Profilo",
        ["Notifications"] = "Notifiche",
        ["No notifications yet"] = "Nessuna notifica",
        ["Comments, reactions, and replies will land here."] = "Commenti, reazioni e risposte appariranno qui.",
        ["new"] = "nuova",
        ["New story"] = "Nuova storia",
        ["New full story"] = "Scrivi una storia completa",
        ["+ New Story"] = "+ Nuova storia",
        ["+ New story"] = "+ Nuova storia",
        ["Quick story"] = "Scrivi una storia",
        ["Quick Story"] = "Scrivi una storia",
        ["+ Quick Story"] = "+ Scrivi una storia",
        ["+ Quick story"] = "+ Scrivi una storia",
        ["New message"] = "Nuovo messaggio",
        ["Latest"] = "Più recenti",
        ["Popular"] = "Più visti",
        ["Recent"] = "Più recenti",
        ["Chronological"] = "Ordine cronologico",
        ["Family"] = "Famiglia",
        ["Recently active"] = "Attivi",
        ["Recently Active"] = "Attivi",
        ["My Network"] = "La mia rete",
        ["Tagged"] = "Menzionato",
        ["Requests"] = "Richieste",
        ["See everyone"] = "Vedi tutti",
        ["No recent activity yet."] = "Nessuna attività recente.",
        ["On This Day"] = "Accadde oggi",
        ["On this day"] = "Accadde oggi",
        ["Coming Soon"] = "Prossimamente",
        ["Invite a friend"] = "Invita un amico",
        ["Invite"] = "Invita",
        ["Share a life story or memory"] = "Condividi una storia della tua vita o un ricordo",
        ["Share your story with someone who was there"] = "Condividi la tua storia con qualcuno che c'era",
        ["Welcome"] = "Benvenuto",
        ["Welcome,"] = "Benvenuto,",
        ["My Drafts"] = "Le mie bozze",
        ["Working Index"] = "Indice di lavoro",
        ["Deleted Stories"] = "Storie cancellate",
        ["Admin Panel"] = "Pannello admin",
        ["User Agreement"] = "Termini di servizio",
        ["Welcome back"] = "Bentornato",
        ["My Profile"] = "Il mio profilo",
        ["Profile & Settings"] = "Profilo e impostazioni",
        ["Export"] = "Scarica la tua storia",
        ["Export your stories"] = "Scarica la tua storia",
        ["Year"] = "Anno",
        ["Month"] = "Mese",
        ["Day"] = "Giorno",
        ["est."] = "ca.",
        ["(est.)"] = "(ca.)",
        ["Estimated"] = "Stimata",
        ["Date is estimated"] = "Data stimata",
        ["Story Date:"] = "Data della storia:",
        ["Story Date"] = "Data della storia",
        ["Title (optional)"] = "Titolo (facoltativo)",
        ["Location (optional)"] = "Luogo (facoltativo)",
        ["Start telling your story… you can paste images directly. Take your time."] = "Inizia a raccontare la tua storia… puoi incollare le immagini direttamente. Prenditi il tuo tempo.",
        ["Start telling your story…"] = "Inizia a raccontare la tua storia…",
        ["Tag people"] = "Aggiungi persone",
        ["Tag People"] = "Aggiungi persone",
        ["Tag friends"] = "Aggiungi amici",
        ["Tag people in this story"] = "Aggiungi persone in questa storia",
        ["Tag someone in your network..."] = "Aggiungi qualcuno della tua rete...",
        ["Publish Story"] = "Pubblica la storia",
        ["Publish story"] = "Pubblica la storia",
        ["Save as Draft"] = "Salva come bozza",
        ["Save as draft"] = "Salva come bozza",
        ["Save Draft"] = "Salva bozza",
        ["My Drafts"] = "Le mie bozze",
        ["My drafts"] = "Le mie bozze",
        ["● Public"] = "● Pubblico",
        ["● Acquaintances"] = "● Conoscenti",
        ["● Friends"] = "● Amici",
        ["● Family"] = "● Famiglia",
        ["🔒 Only Me"] = "🔒 Solo io",
        ["Only Me"] = "Solo io",
        ["Memory Music"] = "Musica del ricordo",
        ["Attach media"] = "Allega media",
        ["Add Media"] = "Aggiungi media",
        ["Done"] = "Fatto",
        ["Post"] = "Pubblica",
        ["Photos or videos"] = "Foto o video",
        ["Select multiple if you like — they'll be saved with the story."] = "Selezionane più di uno se vuoi — saranno salvati con la storia.",

        // Chat dock
        ["Send feedback to admin"] = "Invia un feedback all'amministratore",
        ["Bugs, ideas, wishes — goes straight to Kronoadmin."] = "Bug, idee, desideri — arrivano direttamente a Kronoadmin.",
        ["What would you like us to know?"] = "Cosa vuoi farci sapere?",
        ["Sending…"] = "Invio in corso…",
        ["Thanks! Sent to Kronoadmin."] = "Grazie! Inviato a Kronoadmin.",
        ["Failed to send."] = "Invio non riuscito.",
        ["Could not send."] = "Impossibile inviare.",
        ["Open all →"] = "Apri tutti →",
        ["No messages yet"] = "Nessun messaggio ancora",
        ["Loading conversations…"] = "Caricamento conversazioni…",
        ["Loading messages…"] = "Caricamento messaggi…",
        ["Message…"] = "Messaggio…",
        ["Could not load messages."] = "Impossibile caricare i messaggi.",
        ["Network error. Please try again."] = "Errore di rete. Riprova.",
        ["Open the full inbox"] = "Apri la posta in arrivo",
        ["Open full chat"] = "Apri la chat intera",
        ["Back to messages"] = "Torna ai messaggi",
        ["Open messages"] = "Apri i messaggi",

        // Footer + cookies
        ["An independent passion project, in beta."] = "Un progetto indipendente di passione, in beta.",
        ["Tip the creator"] = "Donazione",
        ["Tip the Creator"] = "Donazione",
        ["Help us stay ad-free"] = "Aiutaci a rimanere senza pubblicità",
        ["A small tip keeps the lights on"] = "Una piccola donazione ci aiuta",
        ["A small tip keeps the lights on."] = "Una piccola donazione ci aiuta.",
        ["About"] = "Informazioni",
        ["Start here"] = "Inizia qui",
        ["FAQ"] = "FAQ",
        ["Privacy"] = "Privacy",
        ["Terms"] = "Termini",
        ["User Agreement"] = "Termini di servizio",
        ["Acceptable use"] = "Uso accettabile",
        ["Acceptable Use"] = "Uso accettabile",
        ["Cookie notice"] = "Avviso sui cookie",
        ["OK"] = "OK",

        // Settings: language picker
        ["Display language"] = "Lingua dell'interfaccia",
        ["Choose how Kronoscript talks back to you."] = "Scegli la lingua con cui Kronoscript ti risponde.",
        ["English"] = "Inglese",
        ["Italian"] = "Italiano",
        ["Switch to English"] = "Passa all'inglese",
        ["Switch to Italian"] = "Passa all'italiano",

        // Common alerts
        ["Saved."] = "Salvato.",
        ["Something went wrong."] = "Qualcosa è andato storto.",
        ["Please try again."] = "Per favore riprova.",

        // Posts
        ["Posted"] = "Pubblicato",
        ["edited"] = "modificato",
        ["Public"] = "Pubblico",
        ["Friends only"] = "Solo amici",
        ["Family only"] = "Solo famiglia",
        ["Acquaintances"] = "Conoscenti",
        ["Private"] = "Privato",
        ["Translate"] = "Traduci",
        ["Show original"] = "Mostra originale",
        ["Connect your memory"] = "Collega il tuo ricordo",
    };
}

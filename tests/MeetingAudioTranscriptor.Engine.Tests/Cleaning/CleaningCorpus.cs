// Realistic business-meeting sentences without any disfluency, per language, and the helper that turns text into words.
using System;
using System.Linq;
using MeetingAudioTranscriptor.Engine.Recognition;

namespace MeetingAudioTranscriptor.Engine.Tests.Cleaning;

/// <summary>Sentences that contain no filler, no stutter and no false start, so a cleaner must change none of them.</summary>
internal static class CleaningCorpus
{
    /// <summary>Builds recognised words from space-separated text; each keeps its casing and attached punctuation.</summary>
    public static RecognizedWord[] Words(string text) =>
        text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select((t, i) => new RecognizedWord(t, i * 0.3, (i * 0.3) + 0.25, 0.9f))
            .ToArray();

    /// <summary>The English sentences.</summary>
    public static string[] English { get; } = Lines("""
        The budget for the second quarter is 450,000 euros.
        We agreed to ship the release on March 14.
        Anna will send the contract to the client by Friday.
        I think the vendor quoted 12 percent more than last year.
        Please book the meeting room for 3 p.m. on Tuesday.
        The steering committee approved the plan on 2 June.
        Our churn rate fell from 4.2 percent to 3.1 percent.
        Tom needs access to the staging server before the demo.
        We decided not to renew the licence with Contoso.
        Can you confirm the invoice number 20931 today?
        The audit starts on the first of October and lasts two weeks.
        I will not approve any spending above 5,000 pounds.
        Maria owns the migration and Paul owns the rollback plan.
        The deadline for the proposal is the 9th of December.
        We have 37 open tickets and 5 of them are critical.
        The new hire starts on Monday and sits next to Jonas.
        Marketing wants a launch event in Berlin in September.
        Let us move the review to Thursday morning.
        The supplier promised delivery within 10 working days.
        Revenue grew by 8 percent in the northern region.
        Nobody objected, so the proposal is accepted.
        Sarah will present the results to the board on 21 November.
        We should hire two more engineers before the summer.
        The server migration is scheduled for the weekend of the 18th.
        He never answered the email about the penalty clause.
        The cost per user is 3.50 dollars per month.
        Our target is 95 percent availability by the end of the year.
        Peter will check the figures with finance tomorrow.
        This contract expires in April 2027.
        We are going to launch in France and Germany first.
        The workshop takes place in Lyon on 5 and 6 May.
        Please send the minutes to everyone in the project team.
        The team agreed to freeze the scope until the pilot ends.
        I promise to deliver the first draft by next Wednesday.
        Nothing in the report suggests a security problem.
        The customer asked for a discount of 15 percent.
        We need a decision on the vendor before the end of the week.
        Lucas and Emma will visit the plant in Munich.
        The pilot ran for six weeks with 120 users.
        Prices will rise on the first of January.
        What is the status of ticket 4417?
        Who will take over the project when Laura leaves?
        The warehouse in Rotterdam handles 30,000 parcels a day.
        We will meet again on 12 February at nine thirty.
        Finance rejected the request because the budget is exhausted.
        Everyone must complete the security training by 30 June.
        The new tool saves about four hours per person every week.
        Our biggest risk is the dependency on a single supplier.
        Daniel committed to fixing the login bug this sprint.
        The conference costs 2,400 euros per attendee.
        I suggest we postpone the launch until the tests pass.
        The bank requires two signatures for every transfer above 10,000 euros.
        Is the printer in building B still broken?
        We closed the deal with Northwind for 1.2 million dollars.
        The legal team will review clause 14 before signing.
        Please use the shared folder for all customer documents.
        Karen will lead the interviews and report on Friday.
        The release notes mention three breaking changes.
        Our next steering meeting is on the 3rd of March.
        The offer is valid until the end of the month.
        You are responsible for the handover to the support team.
        """);

    /// <summary>The French sentences.</summary>
    public static string[] French { get; } = Lines("""
        Le budget du deuxième trimestre est de 450 000 euros.
        Nous avons décidé de livrer la version le 14 mars.
        Marie enverra le contrat au client avant vendredi.
        Je pense que le fournisseur a augmenté ses prix de 12 pour cent.
        Veuillez réserver la salle de réunion pour 15 h mardi.
        Le comité de pilotage a validé le plan le 2 juin.
        Notre taux de départ est passé de 4,2 à 3,1 pour cent.
        Thomas a besoin d'un accès au serveur de test avant la démonstration.
        Nous avons décidé de ne pas renouveler la licence.
        Pouvez-vous confirmer le numéro de facture 20931 aujourd'hui ?
        L'audit commence le premier octobre et dure deux semaines.
        Je n'approuverai aucune dépense supérieure à 5 000 euros.
        Camille pilote la migration et Paul pilote le plan de retour arrière.
        La date limite de la proposition est le 9 décembre.
        Il reste 37 tickets ouverts et 5 sont critiques.
        La nouvelle recrue commence lundi et s'assoit près de Jonas.
        Le marketing veut un événement de lancement à Paris en septembre.
        Déplaçons la revue à jeudi matin.
        Le fournisseur a promis une livraison sous dix jours ouvrés.
        Le chiffre d'affaires a progressé de 8 pour cent dans le nord.
        Personne ne s'y est opposé, la proposition est donc acceptée.
        Sophie présentera les résultats au conseil le 21 novembre.
        Nous devrions recruter deux ingénieurs de plus avant l'été.
        La migration du serveur est prévue pour le week-end du 18.
        Il n'a jamais répondu au courriel sur la clause de pénalité.
        Le coût par utilisateur est de 3,50 euros par mois.
        Notre objectif est une disponibilité de 95 pour cent d'ici la fin de l'année.
        Pierre vérifiera les chiffres avec la comptabilité demain.
        Ce contrat expire en avril 2027.
        Nous lancerons d'abord en France et en Allemagne.
        L'atelier a lieu à Lyon les 5 et 6 mai.
        Merci d'envoyer le compte rendu à toute l'équipe du projet.
        L'équipe a accepté de geler le périmètre jusqu'à la fin du pilote.
        Je m'engage à livrer la première version mercredi prochain.
        Rien dans le rapport ne suggère un problème de sécurité.
        Le client a demandé une remise de 15 pour cent.
        Il nous faut une décision sur le fournisseur avant la fin de la semaine.
        Lucas et Emma visiteront l'usine de Munich.
        Le pilote a duré six semaines avec 120 utilisateurs.
        Les prix augmenteront le premier janvier.
        Quel est l'état du ticket 4417 ?
        Qui reprendra le projet quand Laure partira ?
        L'entrepôt de Rotterdam traite 30 000 colis par jour.
        Nous nous retrouverons le 12 février à neuf heures trente.
        La finance a refusé la demande car le budget est épuisé.
        Chacun doit terminer la formation sécurité avant le 30 juin.
        Le nouvel outil fait gagner environ quatre heures par personne et par semaine.
        Notre plus grand risque est la dépendance à un seul fournisseur.
        Daniel s'est engagé à corriger le bogue de connexion pendant ce sprint.
        La conférence coûte 2 400 euros par participant.
        Je propose de reporter le lancement jusqu'à la réussite des tests.
        La banque exige deux signatures pour tout virement supérieur à 10 000 euros.
        L'imprimante du bâtiment B est-elle toujours en panne ?
        Nous avons conclu l'accord avec Northwind pour 1,2 million d'euros.
        Le service juridique relira la clause 14 avant la signature.
        Merci d'utiliser le dossier partagé pour tous les documents clients.
        Karine mènera les entretiens et fera un retour vendredi.
        Les notes de version mentionnent trois changements majeurs.
        Notre prochain comité de pilotage a lieu le 3 mars.
        L'offre est valable jusqu'à la fin du mois.
        Vous êtes responsable de la passation à l'équipe support.
        """);

    /// <summary>The German sentences.</summary>
    public static string[] German { get; } = Lines("""
        Das Budget für das zweite Quartal beträgt 450.000 Euro.
        Wir haben beschlossen, die Version am 14. März auszuliefern.
        Anna schickt den Vertrag bis Freitag an den Kunden.
        Ich glaube, der Lieferant hat zwölf Prozent mehr verlangt als im Vorjahr.
        Bitte buchen Sie den Besprechungsraum am Dienstag um 15 Uhr.
        Der Lenkungsausschuss hat den Plan am 2. Juni genehmigt.
        Unsere Kündigungsquote ist von 4,2 auf 3,1 Prozent gesunken.
        Tom braucht vor der Demo Zugang zum Testserver.
        Wir haben entschieden, die Lizenz nicht zu verlängern.
        Können Sie die Rechnungsnummer 20931 heute bestätigen?
        Die Prüfung beginnt am ersten Oktober und dauert zwei Wochen.
        Ich genehmige keine Ausgaben über 5.000 Euro.
        Maria verantwortet die Migration und Paul den Rückfallplan.
        Die Frist für das Angebot ist der 9. Dezember.
        Es gibt 37 offene Tickets, davon sind 5 kritisch.
        Die neue Kollegin beginnt am Montag und sitzt neben Jonas.
        Das Marketing wünscht eine Veranstaltung in Berlin im September.
        Verschieben wir die Durchsicht auf Donnerstagvormittag.
        Der Lieferant hat eine Lieferung innerhalb von zehn Arbeitstagen versprochen.
        Der Umsatz ist in der Nordregion um 8 Prozent gewachsen.
        Niemand hat widersprochen, der Vorschlag ist also angenommen.
        Sarah stellt dem Vorstand am 21. November die Ergebnisse vor.
        Wir sollten vor dem Sommer zwei weitere Entwickler einstellen.
        Die Servermigration ist für das Wochenende des 18. geplant.
        Er hat nie auf die Mail zur Vertragsstrafe geantwortet.
        Die Kosten pro Nutzer betragen 3,50 Euro im Monat.
        Unser Ziel sind 95 Prozent Verfügbarkeit bis zum Jahresende.
        Peter prüft morgen die Zahlen mit der Finanzabteilung.
        Dieser Vertrag läuft im April 2027 aus.
        Wir starten zuerst in Frankreich und Deutschland.
        Der Workshop findet am 5. und 6. Mai in Lyon statt.
        Bitte schicken Sie das Protokoll an das gesamte Projektteam.
        Das Team hat zugestimmt, den Umfang bis zum Ende der Pilotphase einzufrieren.
        Ich verspreche, den ersten Entwurf bis nächsten Mittwoch zu liefern.
        Im Bericht deutet nichts auf ein Sicherheitsproblem hin.
        Der Kunde hat einen Rabatt von 15 Prozent verlangt.
        Wir brauchen vor Ende der Woche eine Entscheidung zum Lieferanten.
        Lukas und Emma besuchen das Werk in München.
        Die Pilotphase dauerte sechs Wochen mit 120 Nutzern.
        Die Preise steigen am ersten Januar.
        Wie ist der Stand von Ticket 4417?
        Wer übernimmt das Projekt, wenn Laura geht?
        Das Lager in Rotterdam bearbeitet 30.000 Pakete pro Tag.
        Wir treffen uns am 12. Februar um halb zehn wieder.
        Die Finanzabteilung hat den Antrag abgelehnt, weil das Budget erschöpft ist.
        Alle müssen die Sicherheitsschulung bis zum 30. Juni abschließen.
        Das neue Werkzeug spart etwa vier Stunden pro Person und Woche.
        Unser größtes Risiko ist die Abhängigkeit von einem einzigen Lieferanten.
        Daniel hat zugesagt, den Anmeldefehler in diesem Sprint zu beheben.
        Die Konferenz kostet 2.400 Euro pro Teilnehmer.
        Ich schlage vor, den Start zu verschieben, bis die Tests bestehen.
        Die Bank verlangt zwei Unterschriften für jede Überweisung über 10.000 Euro.
        Ist der Drucker in Gebäude B immer noch defekt?
        Wir haben den Vertrag mit Northwind über 1,2 Millionen Dollar abgeschlossen.
        Die Rechtsabteilung prüft Klausel 14 vor der Unterschrift.
        Bitte nutzen Sie den gemeinsamen Ordner für alle Kundendokumente.
        Karin leitet die Gespräche und berichtet am Freitag.
        Die Versionshinweise nennen drei grundlegende Änderungen.
        Unsere nächste Lenkungsrunde ist am 3. März.
        Das Angebot gilt bis zum Monatsende.
        Sie sind für die Übergabe an das Support-Team verantwortlich.
        """);

    private static string[] Lines(string block) =>
        block.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

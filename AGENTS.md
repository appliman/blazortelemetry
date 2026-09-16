# Instructions du projet

## Regles generales de codage

### Nom des fichiers
- Classes : `PascalCase` (ex. `CustomerService.cs`).
- Interfaces : prefixe `I` (ex. `IProductRepository.cs`).
- Fichiers de tests : suffixe `Tests.cs`.

### Nom des classes et membres
- Classes et records : `PascalCase`.
- Methodes : `PascalCase`.
- Variables locales et champs prives : `_camelCase`.
- Constantes : `UPPER_CASE_SNAKE`.

### Organisation du code
- Une classe par fichier.
- Ne pas utiliser de `#region`.
- Placer les constructeurs en haut de classe.
- Placer les methodes privees en bas du fichier.
- Toujours utiliser les accolades `{}` meme pour les blocs d'une seule ligne.
- Utiliser de preference les primary constructors quand ils simplifient le code.
- Ne pas creer de modeles `Draft` pour l'edition : utiliser directement les entites de `StockAsso2.Datas`.

### Mise en forme
- Indentation : 4 espaces.
- Conserver des espaces autour des operateurs (`a + b`, pas `a+b`).

### Langue de l'application
- Tous les textes visibles, messages de validation, journaux techniques et données de démonstration de BlazorTelemetry doivent être écrits en anglais.
- Ne pas ajouter de nouveau texte d'interface en français.
- Les instructions internes peuvent rester en français et doivent alors utiliser les accents.

### Documentation
- Ne pas creer de fichiers de documentation (`README.md`, `SUMMARY.md`, `QUICKSTART.md`, etc.) sauf demande explicite de l'utilisateur.
- Se concentrer uniquement sur le code et les fichiers de configuration necessaires.

### Demarrage local du site
- Quand l'utilisateur demande de demarrer le site dans Codex, lancer une console visible afin qu'il puisse consulter les logs.
- Demarrer le site avec le hot reload actif, de preference via `dotnet watch`.
- Afficher le site dans le navigateur integre de Codex en version mobile.
- Pour les demandes de modification, privilegier le hot reload et eviter au maximum une recompilation complete du site.

### Regles C#
- Utiliser `async`/`await` partout ou c'est applicable.
- Il n'est pas nécessaire de suffixer les méthodes async avec `Async`.
- Preferer `var` pour les types evidents.
- Ne pas ajouter de `#pragma warning disable` sans justification explicite.
- Dans tous les composants Blazor, placer le code C# avant le markup HTML.
- Passer un `CancellationToken` au maximum possible dans les appels asynchrones.
- Toujours utiliser les accolades `{}` pour les conditions, les boucles et les autres blocs de contrôle.
- Utiliser au maximum les primary constructors quand ils simplifient le code.
- Utiliser un fichier par classe, record ou enum.

### SQLite et LINQ
- Avec SQLite, ne pas effectuer de calcul, conversion ou transformation dans une requête LINQ exécutée en base.
- Précalculer les valeurs en C# avant la requête et les passer en paramètres. Si une opération n'est pas traduisible par SQLite, matérialiser d'abord les résultats puis effectuer le calcul ou le tri côté client.

### Handlers
- Ne jamais lever d'exception dans un handler.
- Si le handler retourne un `CommandResult` ou l'un de ses types dérivés, signaler l'erreur au moyen des `BrokenRules` du résultat.
- Si le handler ne retourne pas de `CommandResult` ou l'un de ses types dérivés, journaliser l'erreur avec le niveau `Error`.

### Gestion des numéros de version
- Utiliser un numéro de version au format `1.2.3.4`.
- Le premier nombre correspond à un changement majeur, comme le passage à une nouvelle version du framework .NET (par exemple de .NET 10 à .NET 11) ou une modification majeure de l'application.
- Le deuxième nombre correspond à un changement du schéma de la base de données, comme l'ajout d'une table ou toute autre évolution du schéma.
- Le troisième nombre correspond aux mises à jour globales, améliorations et nouvelles fonctionnalités. Il augmente toujours de `1` à chaque mise à jour et n'est jamais remis à zéro, afin de conserver le nombre total de mises à jour.
- Le quatrième nombre correspond uniquement aux corrections de bugs. Il augmente de `1` pour chaque correction de bug seule et est remis à zéro lorsque la correction accompagne une mise à jour qui incrémente l'un des trois premiers nombres.

### Formulaires
- Tous les formulaires existants ou a venir doivent utiliser les floating labels Bootstrap 5.3 : https://getbootstrap.com/docs/5.3/forms/floating-labels/.
- Pour les champs de saisie d'un formulaire, utiliser en priorité les composants `SuperInput*` existants quand ils couvrent le besoin.

### Pages Blazor admin
- Dans l'admin, une route doit correspondre a une seule page et a une responsabilite claire.
- Separer les pages de liste des pages d'ajout ou d'edition pour tous les items admin.
- Eviter les pages `*Home.razor` qui combinent liste et edition. Par exemple, remplacer `BrandsHome.razor` par `BrandList.razor` et `BrandEdit.razor`.
- Pour conditionner l'affichage selon les droits, utiliser au maximum des blocs `<AuthorizeView Policy="@nameof(Policies.HasAdminAccess)">` plutôt que des conditions dans le code C#.

### Affichage site web
- Ne jamais créer de décorateurs ni de classes intermédiaires à mapper autour des entités de `StockAsso2.Datas` pour la partie web ; utiliser directement les entités du projet.
- Pour afficher les enums dans le site web, utiliser la methode d'extension `ToFriendlyName()`.
- Dans le front web, toute propriété `Presentation` d'une entité qui implémente `IMarkdownable` doit être rendue avec Markdig et les extensions emoji et Bootstrap.
- Ne jamais afficher la devise en texte `EUR` dans l'interface ; utiliser le symbole `€`.
- Dans le site web, éviter de mettre les textes d'affichage en dur dans des méthodes C# quand ils servent uniquement au rendu. Privilégier les textes directement dans le contenu HTML/Razor pour garder le markup lisible et localiser facilement les messages. Par exemple, éviter des méthodes du type `GetStockLabel`, `GetOptionsLabel` ou `GetCartStatus` qui retournent des messages UI comme `En stock`, `Sans option` ou `Panier en cours`.

## Organisation de la solution

- `StockAsso2.Datas` contient les classes de base (POCO), les enums et les superenums. Cette assembly ne doit pas avoir de dependance applicative.
- `StockAsso2.EntityFramework`, `StockAsso2.SqlServer` et `StockAsso2.Sqlite` contiennent la configuration Entity Framework et referencent les classes de `StockAsso2.Datas`.
- Toutes les tables utilisees doivent etre configurees dans `OnModelCreating` ou via les extensions de configuration du modele existantes.
- `StockAsso2.Contracts` contient les requests liees aux POCO de `StockAsso2.Datas`.
- Les requests implementent le pattern mediator utilise par le projet.
- Chaque entite presente dans `StockAsso2.Datas` a son propre repertoire de contracts avec les requests CRUD attendues :
  - `Create(Entity)Request` pour la creation d'une entite.
  - `Delete(Entity)Request` pour la suppression d'une entite.
  - `(Entity)ListFilter`, qui implemente `IListFilter` et retourne une liste paginee de l'entite via `PagedList<IEnumerable<Entity>>`.
  - `Save(Entity)Request` pour la persistance des donnees, avec un resultat de persistance et la liste des regles de validation echouees en cas d'erreur.
  - `(Entity)ChangedNotification`, heritant de `EntityChangedNotificationBase`, pour notifier l'ajout, la modification ou la suppression d'une entite.
- Les projets `StockAsso2.*Api` contiennent les handlers concrets lies au mediator et effectuent la liaison a la base de donnees.
- Avant chaque sauvegarde, valider l'entite avec FluentValidation. Les validateurs sont dans les repertoires `Validators` ou `Validator` existants.
- Apres chaque sauvegarde reussie d'une entite, publier une notification globale pour indiquer qu'une entite a ete creee, modifiee ou supprimee.
- `StockAsso2.WebApp` est un site Blazor Server pour l'affichage public aux utilisateurs finaux et un dossier Admin est dédié pour le backoffice.
- `StockAsso2.Tests` contient les tests unitaires et les tests d'integration.

## Migrations et schema

- Ne pas ajouter de scripts SQL de migration ecrits a la main pour les changements de schema.
- L'evolution du schema doit passer par les migrations design-time.
- Toute modification du schéma de données doit entraîner la création d'une migration dans le projet `StockAsso2.Sqlite`.

## Confirmation des actions

- Toujours demander confirmation a l'utilisateur avant d'effectuer des actions qui ne sont pas explicitement demandees, meme si elles semblent logiques ou coherentes.
- Ne jamais creer, modifier ou supprimer de fichiers supplementaires sans accord explicite de l'utilisateur.

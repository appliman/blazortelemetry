# BlazorTelemetry

BlazorTelemetry est un collecteur et dashboard OpenTelemetry autonome pour les petites équipes .NET qui veulent consulter leurs logs, traces et métriques sans exploiter une pile Grafana. Il fonctionne comme composant Blazor Server embarqué ou comme application Docker autonome.

Le produit reçoit OTLP HTTP/protobuf, conserve les données dans SQLite et relie les signaux par service, trace et span. Il fournit des tableaux partagés, des alertes avec incidents persistants et des notifications webhook, SMTP ou ntfy. La première version vise 1 à 10 applications et environ 100 éléments par seconde sur une instance unique.

Les utilisateurs sont des développeurs et exploitants. Un Lecteur consulte les signaux et ajuste ses filtres temporaires. Un Administrateur gère les comptes, les clés d’ingestion, les tableaux, les règles d’alerte et la rétention. Le mode autonome ne propose aucune inscription publique.

Le succès se mesure par une installation en un seul package ou conteneur, une navigation rapide entre signaux corrélés, l’absence de perte silencieuse et la conservation après redémarrage. Le produit ne remplace pas un stockage distribué, PromQL, LogQL, la haute disponibilité ou l’orchestration Aspire.

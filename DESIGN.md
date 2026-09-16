# Direction visuelle

## THESIS

Une console d'exploitation dense et calme qui permet de constater l'état de la collecte, puis de passer d'un signal à son contexte corrélé sans quitter le dashboard.

## OWN-WORLD

Le produit vit dans l'univers d'un observatoire technique .NET : surfaces bleu nuit plates, séparateurs précis, IBM Plex Sans locale, chiffres tabulaires et trois couleurs fonctionnelles. Le vert menthe indique l'activité, le bleu la corrélation, l'ambre l'attente et le rouge l'incident. Les graphiques ECharts et la typographie sont embarqués dans le package.

## STORY

Le dashboard opérationnel est la vue d'entrée : durée et taux d'erreur, activité courante, compteurs, protocoles et endpoints. La Synthèse expose ensuite le collecteur, les volumes et la carte des services. Logs, Traces et Métriques permettent d'enquêter avec les mêmes filtres de service et de temps. Alertes transforme l'enquête en suivi d'équipe. Administration termine le parcours par l'accès, la rétention et les clés d'ingestion.

## FIRST VIEWPORT

Le premier écran doit toujours montrer l'état du collecteur, les filtres, la durée des requêtes, le taux d'erreur et les indicateurs d'activité. En mode autonome, le dashboard occupe toute la surface. En mode embarqué, sa hauteur suit son contenu et ne crée pas de vide artificiel.

## FORM

FORM-SEED: observatory-grid-017

Le vocabulaire de forme utilise une grille compacte, des angles modérés, des bordures discrètes et peu d'ombres. Les tableaux, cascades et cartes de signaux gardent une lecture horizontale nette. Sur mobile, la navigation devient une rangée défilable contenue dans le composant et les filtres occupent toute la largeur.

## SIGNATURE INTERACTION

Le pivot log-vers-trace est l'interaction signature : un identifiant de trace ouvre immédiatement la cascade corrélée. Le bouton En direct suspend ou reprend le rafraîchissement groupé sans perdre les filtres.

## MOTION

Le mouvement est réservé au chargement et aux changements d'état utiles, avec des transitions brèves. `prefers-reduced-motion` neutralise animations et transitions.

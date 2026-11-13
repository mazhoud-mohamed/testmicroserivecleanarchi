// Les mesures (compteurs, jauge) sont des objets statiques partagés par tout le processus : deux tests qui publient
// des messages en parallèle fausseraient les totaux lus par un MeterListener. On exécute donc ces tests l'un après l'autre.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

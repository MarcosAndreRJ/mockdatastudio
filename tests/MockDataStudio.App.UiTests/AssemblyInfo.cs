using Xunit;

// Testes de interface controlam janelas reais e o foco do desktop: nunca rodam em paralelo.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

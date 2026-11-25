using Subterra.Core.Model;
using Subterra.Core.Radar;
using Xunit;

namespace Subterra.Core.Tests.Radar
{
    public class MaterialsTests
    {
        [Fact] public void IceDefault() => Assert.Equal(0.01, Materials.Attenuation60(new LayerSpec { Density = 1.0 }), 9);

        [Fact] public void LooseDefault() => Assert.Equal(0.1, Materials.Attenuation60(new LayerSpec { Density = 1.4 }), 9);

        [Fact] public void RockDefault() => Assert.Equal(0.2, Materials.Attenuation60(new LayerSpec { Density = 2.6 }), 9);

        [Fact] public void BoundaryIsRock() => Assert.Equal(0.2, Materials.Attenuation60(new LayerSpec { Density = 2.2 }), 9);

        [Fact] public void ExplicitWins() => Assert.Equal(0.05, Materials.Attenuation60(new LayerSpec { Density = 2.6, Attenuation = 0.05 }), 9);

        [Fact] public void ScalesWithFrequency() => Assert.Equal(0.2 * 500 / 60, Materials.Attenuation(new LayerSpec { Density = 2.6 }, 500), 9);

        [Fact] public void IcePermittivity() => Assert.Equal(4, Materials.Permittivity(new LayerSpec { Density = 0.9 }));

        [Fact] public void RockPermittivity() => Assert.Equal(6, Materials.Permittivity(new LayerSpec { Density = 3.0 }));
    }
}

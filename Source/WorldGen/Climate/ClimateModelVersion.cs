namespace Planets.WorldGen.Climate
{
    /// <summary>
    /// Bump Current in the same change as a climate formula, and say why here.
    /// 1: save format v2. Gameplay pack plus bake record. Diagnostics are rebuilt, not saved.
    /// 2: distance, currents, and ocean evaporation use the true ocean. Lakes evaporate at 0.4 of the ocean rate.
    /// 3: tile north points north. Trades tilt equatorward, westerlies poleward, polar easterlies equatorward. A lone ITCZ stays weak.
    /// 4: annual mean temperature shifts with axial tilt. Zero at Normal (22.5°). Higher tilt warms the poles and cools the equator.
    /// 5: coastal blend is applied at sea level, then lapse rate and snow albedo cool the result.
    /// 6: boundary currents use a latitude plateau and only the nearer coast, within 8 hops.
    /// 7: monsoon inland reach sets the step decay; moisture capacity is not flat above 19.6°C; rainfall noise follows the world seed.
    /// 8: moisture capacity rises about 7% per degree. The ceiling is whatever that slope reaches at 45°C.
    /// 9: moisture is swept upstream-first to a steady state. Forced condensation can fall to 0.2%, so subtropical sinking air can stay dry.
    /// 10: summer and winter winds. The regime table slides with axial tilt. Monsoon rain is the summer wind turning onshore.
    /// 11: land rainfall is scaled back to the measured median. 30% coverage and a full sphere each keep their own row.
    /// 12: enclosed water moderates the nearby shore and evaporates more as the body gets larger. The true ocean is unchanged.
    /// 13: land evaporation recycles rainfall, up to the previous temperature ceiling. Ocean, lakes, inland seas, and the monsoon source are unchanged.
    /// 14: inland seasonal amplification is 0.38 of the previous excess over the coast. The obliquity scale is unchanged, and ocean seasonality is unchanged.
    /// 15: the ocean latitude curve is 6°C cooler at the equator and 6.5°C warmer at the pole than the temperature preset. Land endpoints are unchanged.
    /// 16: land preset on Normal is −29.5°C at the pole and 31°C at the equator, and the land exponent is 1.00. Ocean endpoints are held at the model 15 curve.
    /// 17: the seasonal pressure shift moves only the equatorial trough, within 18° of the thermal equator. The subtropical crests stay on geographic latitude.
    /// 18: land-sea temperature contrast is smoothed over 400 km before it changes the pressure. The previous 1,200 km removed the contrast the monsoon needs.
    /// 19: the heat low is 5 hPa per degree. At 1 hPa per degree, June India was still a northeast trade after the contrast survived 400 km of smoothing.
    /// 20: the heat low is 2.5 hPa per degree. At 5, June India and northern Australia blew the right way and several times too fast.
    /// 21: warm and cold departures no longer share one strength. Warm air moves both layers at 3.5 hPa per degree. Cold air moves only the lower layer, at 1 hPa per degree. The lower layer turns along a mountain wall instead of crossing it. A plateau at or above 2 km that is not a wall uses the upper layer.
    /// 22: the 3.5 hPa per degree applies to warm land only. Warm ocean uses the cold strength, 1 hPa per degree. A coastal cell blends by its land fraction. v13 had put the land heat low on winter ocean as well.
    /// 23: warm land is 2.5 hPa per degree. At 3.5, with the ocean at 1, June India stayed from the southwest and the median speed was about 9 m/s. Warm ocean stays at 1.
    /// 24: those strengths apply to the anomaly smoothed again to about 1,500 km. The leftover after the 400 km smooth moves both layers at 0.5 hPa per degree, with no land bonus. v15's box average was right and the tiles inside it cancelled.
    /// 25: the 400 km leftover is 1 hPa per degree. At 0.5, June India's speed matched Earth and the southwest turn was gone.
    /// 26: that 1 hPa per degree is the inland leftover. Within 800 km of the true ocean the leftover is 2.5. v17 moved the monsoon and Tibet's walls together when one strength applied everywhere.
    /// 27: the coastal leftover is 1 again, the same as inland. v18 made June India more eastward and faster, and the northward wind went slightly negative.
    /// 28: the latitude pressure profile is multiplied by 0.7. On the v19 replay that turned June India's northward wind without raising the speed. The 18° trough is unchanged.
    /// 29: the broad smooth is 2,500 km, the profile scale is 0.6, and the inland leftover is 0.75. The coast leftover stays 1. This is the v20 joint sweep that kept June India and brought December Australia back.
    /// 30: friction and surface speed measured from ERA5 pressure (ocean drag 1e-5, surface wind 0.7 of the layer wind over ocean and 0.5 over land). The latitude profile is the southern hemisphere's at full strength, scaled by each band's ocean share, plus the measured winter-minus-summer shape. Temperature departures are measured from the ocean at the same latitude. Cold strength 2. On the v21 replay the score went from 2.34 to 1.53, with the trades and westerlies near Earth's.
    /// 31: on the Earth heightmap the seeded latitude wobble is off, so every seed gives the same Earth climate. Noise worlds are unchanged. v21 and v22 differed by about 0.8°C per cell from the wobble alone.
    /// 32: with the new climate on, continentality is carried by the wind (WS29). Ocean air fades over 1,500 km of land travel, every coast keeps a 30 km breeze, and local winter weighs half. The coastal blend pulls toward the sea the air came from, at half the yearly ocean-air share. Fully maritime land swings like the ocean at its latitude; fully continental land keeps the old maximum. Temperature, circulation, and ocean air run two coupled rounds. The old path is unchanged.
    /// 33: the cold high is gated by the air's own temperature, full at -15°C and below and none at +5°C and above, before smoothing. Warm subtropical land in winter makes no cold high; Siberia keeps its own. On the v22 replay the December Indian Ocean 0–10°N went from −4.8 to −2.4 m/s (ERA5 −1.7).
    /// 34: ocean air rides the circulation's upper layer at layer speed, plus an eastward storm drift equal to the band's mean westerly, and fades over 3,000 km. The share is solved to convergence by upstream Gauss-Seidel sweeps; model 32's single Krylov step had left it far too continental and took about 13 minutes per round. On the v23 offline solve the seasonal-range error against CRU, land 30–70° below 2 km, fell from 8.0 (nearest coast) to 6.3.
    /// 35: the land surface cools 4.5 °C per km of height instead of 6.5 (the circulation uses the same rate, so its pressure is unchanged). With the new climate on, the ocean's seasonal swing is the latitude curve's swing times 0.39 + 0.66 × the share of air over the water that crossed land, carried on the same transport wind with a 2,000 km fade over water. On v24: land annual RMS against CRU 3.8 → 3.0 offline; ocean swing RMS against ERA5 2 m temperature 6.8 → 3.2; wind replay score 1.63 → 1.47.
    /// 36: two-layer moisture budget and switch-over (WS30 + WS31, item 25e). New climate defaults on. Column depth exp(-z/8 km) in geography; summer continentality gradient and 50–70°N summer cooling in seasonality; land-confined seasonal trough and plateau-driven monsoon trough shift with positive-only local warm residual in circulation; two-layer moisture budget (0.44/0.56 carrier with crest-blocked mass-continuity return flow, rising-air trigger capped at D95 2.5e-6 s⁻¹, directional surface-wind slope shift, westerly storm-track eddy mixing K, humid-air saturation ramp, storm-belt ocean evaporation, FAO-56 solar-declination PET, and 4-round Fu-Budyko land recycling).
    /// 37: spherical solar geometry (sunset hour angle, day-length fraction, noon solar altitude, and multi-month polar-night depth) drives polar-night radiative cooling, 24h midnight-sun tundra warming, permanent-ice summer co-albedo damping, snapshot daily temperature range in PET, and Bowen sensible surface heating on dry land after the moisture budget; wind is rebuilt after the final coupling round.
    /// 38: analytical 3-cell Held-Hou/Ferrel baroclinic ocean pressure profile and seasonal pressure delta (retiring the three empirical ERA5 ocean pressure lookup tables); wind-driven subtropical and subpolar ocean boundary currents and upwelling; seasonal wind-carried continentality in circulation (retiring the straight-line oceanDistKm summer hack and duplicate high-latitude summer cooling); high-plateau marine boundary layer decoupling; and a 2-round moisture pre-pass feeding continental monsoon condensation latent heating into circulation while keeping dry-desert skin heat on the boundary layer.
    /// 39: humid-air rain and lower-layer convergence rain limited to min(column, saturation capacity) (--cap-rain) so cold high-elevation summits do not rain a warm lowland column; low-elevation coastal convergence rain capped at the humid-air timescale (--coast-clock); June/December calendar alignment in circulation rain heating; and wider OverallRainfall preset steps coupled to condensation threshold and post-solve rainfall scaling.
    /// </summary>
    public static class ClimateModelVersion
    {
        public const int Current = 39;
    }
}

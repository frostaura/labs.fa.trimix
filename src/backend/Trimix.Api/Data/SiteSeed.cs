using System.Security.Cryptography;
using System.Text;

namespace Trimix.Api.Data;

/// <summary>
/// EF-seeded initial site dataset: ~50 real, famous dive sites with factual (approximate,
/// publicly known) coordinates, depth ranges, types, hazards, marine life, and access notes.
/// Ids are deterministic (SHA-256 of the site name, truncated) so HasData stays stable across
/// migrations. This is original, honestly-approximate reference data — the licensed large-scale
/// seed dataset remains a post-slice blocking task (PRD).
/// </summary>
public static class SiteSeed
{
    /// <summary>Deterministic seed-site id derived from the site name.</summary>
    public static Guid IdFor(string name) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes($"trimix.site.seed:{name}")).AsSpan(0, 16));

    public static IReadOnlyList<Site> All { get; } = Build();

    private static List<Site> Build()
    {
        var sites = new List<Site>();

        void Add(
            string name, double lat, double lng, double depthMin, double depthMax,
            string type, string hazards, string marineLife, string access) =>
            sites.Add(new Site
            {
                Id = IdFor(name),
                Name = name,
                Lat = lat,
                Lng = lng,
                DepthMinM = depthMin,
                DepthMaxM = depthMax,
                Type = type,
                HazardsCsv = hazards,
                MarineLifeCsv = marineLife,
                AccessNotes = access,
                Status = SiteStatuses.Live,
                Source = SiteSources.Seed,
            });

        // Red Sea
        Add("SS Thistlegorm", 27.8133, 33.9203, 16, 32, "wreck",
            "Strong currents,Boat traffic,Penetration hazards",
            "Batfish,Barracuda,Trevally,Crocodilefish,Stonefish",
            "Boat from Sharm El Sheikh or liveaboard; advanced certification recommended; early start beats crowds.");
        Add("Blue Hole Dahab", 28.5720, 34.5372, 7, 40, "sinkhole",
            "Depth beyond recreational limits,The Arch overhead at ~56 m,Peer-pressure depth incidents",
            "Fusiliers,Cornetfish,Triggerfish,Unicornfish",
            "Shore entry via El Bells or the lagoon saddle; stay strictly within your certification limits.");
        Add("Shark and Yolanda Reef", 27.7231, 34.2506, 10, 40, "wall",
            "Strong currents,Down-currents at the corner",
            "Grey reef sharks,Napoleon wrasse,Snapper schools,Barracuda,Batfish",
            "Boat from Sharm El Sheikh (Ras Mohammed National Park); usually dived as a drift.");
        Add("Elphinstone Reef", 25.3129, 34.8598, 12, 40, "wall",
            "Strong currents,Blue-water exposure,Oceanic whitetip encounters",
            "Oceanic whitetip sharks,Hammerhead sharks,Soft corals,Anthias",
            "Zodiac or liveaboard from Marsa Alam; advanced divers only in season.");
        Add("Sha'ab Rumi South Plateau", 19.9390, 37.4130, 9, 30, "reef",
            "Currents on the plateau edge,Remote location",
            "Grey reef sharks,Bumphead parrotfish,Jacks,Remains of Cousteau's Conshelf II",
            "Sudan liveaboard only; plateau at 9–30 m with shark action at the drop-off.");
        Add("Umbria Wreck", 19.6220, 37.2380, 5, 36, "wreck",
            "Penetration hazards,Live WWII ordnance in holds",
            "Glassfish,Batfish,Hard coral growth on hull",
            "Wingate Reef off Port Sudan; sheltered inside the reef, liveaboard access.");

        // Mediterranean & North Atlantic
        Add("Um El Faroud", 35.8190, 14.4520, 15, 36, "wreck",
            "Depth,Occasional surge,Penetration hazards",
            "Barracuda,Amberjack,Scorpionfish,Moray eels",
            "Shore entry at Wied iz-Zurrieq, Malta; long swim — plan gas accordingly.");
        Add("Blue Hole Gozo", 36.0537, 14.1883, 8, 40, "arch",
            "Surge in the chimney,Overhead at the arch",
            "Octopus,Parrotfish,Moray eels,Nudibranchs",
            "Shore entry over limestone at Dwejra, Gozo; rough seas make entry/exit hazardous.");
        Add("Zenobia Wreck", 34.9016, 33.6535, 16, 42, "wreck",
            "Penetration hazards,Disorientation inside cargo decks,Depth",
            "Grouper,Barracuda,Jacks,Turtles",
            "Short boat ride from Larnaca, Cyprus; one of the world's top wreck dives.");
        Add("Silfra Fissure", 64.2559, -21.1160, 7, 18, "fissure",
            "2–4°C water,Drysuit exposure,Site current toward the lagoon",
            "Bright green troll hair algae,Occasional Arctic char",
            "Thingvellir National Park, Iceland; guided dives only, drysuit certification required.");
        Add("SMS Markgraf", 58.8900, -3.1550, 24, 45, "wreck",
            "Depth,Cold dark water,Protected war grave — no penetration",
            "Wrasse,Pollack,Anemones,Dead man's fingers",
            "Day boat or liveaboard from Stromness, Scapa Flow, Orkney; advanced/tec profile.");

        // Pacific — Oceania
        Add("Rainbow Warrior Wreck", -34.9800, 173.9370, 18, 26, "wreck",
            "Surge,Visibility varies",
            "Anemones covering the hull,John Dory,Kingfish,Moray eels",
            "Boat from Matauri Bay, Cavalli Islands, New Zealand.");
        Add("Blue Maomao Arch", -35.4700, 174.7360, 5, 24, "arch",
            "Surge through the arch",
            "Blue maomao schools,Stingrays,Kelp forest life",
            "Boat from Tutukaka to the Poor Knights Islands, New Zealand; marine reserve.");
        Add("SS Yongala", -19.3050, 147.6210, 14, 28, "wreck",
            "Strong currents,No penetration (protected historic wreck),Exposed open-water site",
            "Giant grouper,Bull rays,Sea snakes,Turtles,Trevally,Eagle rays",
            "Boat from Ayr or Townsville, Queensland; advanced certification recommended.");
        Add("Cod Hole", -14.6600, 145.6600, 10, 25, "reef",
            "Mild currents",
            "Potato cod,Coral trout,Sweetlips,Reef sharks",
            "Ribbon Reefs, Great Barrier Reef; liveaboard from Cairns.");
        Add("North Horn", -13.8090, 146.5540, 10, 40, "wall",
            "Strong currents,Blue-water exposure,Shark feed dives",
            "Grey reef sharks,Silvertips,Hammerheads,Dogtooth tuna",
            "Osprey Reef, Coral Sea; liveaboard only.");
        Add("Navy Pier", -21.8160, 114.1830, 8, 15, "artificial",
            "Restricted naval facility,Low visibility on big tides",
            "Wobbegongs,Grey nurse sharks,Frogfish,Nudibranchs,Octopus",
            "Exmouth, Western Australia; access only via the licensed operator, ID required.");
        Add("Great White Wall", -16.7900, -179.9500, 10, 35, "wall",
            "Strong tidal currents in Somosomo Strait",
            "White soft corals,Anthias,Reef sharks,Barracuda",
            "Boat from Taveuni, Fiji; timed to slack tide on the Rainbow Reef.");
        Add("Beqa Lagoon Shark Arena", -18.3900, 178.1300, 15, 30, "shark dive",
            "Baited shark dive,Multiple large bull sharks",
            "Bull sharks,Tiger sharks (occasional),Giant trevally,Remoras",
            "Boat from Pacific Harbour, Fiji; structured feed dive with strict positioning rules.");
        Add("SS President Coolidge", -15.5240, 167.1850, 20, 70, "wreck",
            "Depth,Penetration hazards,Deco obligations on deeper sections",
            "Lionfish,Barracuda,Moray eels,The Lady statue,Coral encrusted holds",
            "Shore entry near Luganville, Espiritu Santo, Vanuatu; deep sections are technical dives.");

        // Micronesia
        Add("Fujikawa Maru", 7.4458, 151.8010, 9, 34, "wreck",
            "Penetration hazards,Silt-out risk inside holds",
            "Zero fighter remains in holds,Soft corals,Glassfish,Anemonefish",
            "Chuuk (Truk) Lagoon; boat from Weno, guided wreck protocols apply.");
        Add("Shinkoku Maru", 7.3990, 151.7500, 12, 38, "wreck",
            "Penetration hazards,Depth on the engine room",
            "Soft coral gardens on masts,Operating theatre artifacts,Batfish",
            "Chuuk (Truk) Lagoon; boat from Weno; one of the lagoon's most coral-rich wrecks.");
        Add("USS Saratoga", 11.5880, 165.4270, 30, 52, "wreck",
            "Technical depths,Remote location,Deco diving only",
            "Aircraft in hangars,Sharks patrolling the flight deck,Big pelagics",
            "Bikini Atoll, Marshall Islands; technical liveaboard expeditions only.");
        Add("Blue Corner", 7.1350, 134.2200, 8, 30, "wall",
            "Strong unpredictable currents,Reef hook required",
            "Grey reef sharks,Napoleon wrasse,Barracuda tornado,Eagle rays,Jacks",
            "Boat from Koror, Palau; hook in at the plateau edge and watch the parade.");
        Add("German Channel", 7.1180, 134.2560, 12, 25, "channel",
            "Boat traffic in the channel,Current at the cleaning stations",
            "Manta rays,Grey reef sharks,Garden eels,Snapper schools",
            "Boat from Koror, Palau; timed around manta cleaning-station activity.");
        Add("Yonaguni Monument", 24.4360, 122.9380, 5, 25, "rock formation",
            "Strong currents,Surge near the terraces",
            "Hammerhead sharks in winter,The carved-looking megalith terraces",
            "Boat from Yonaguni Island, Japan; best conditions November–June.");

        // Hawaii & Americas (Pacific)
        Add("Manta Ray Night Dive Kona", 19.7370, -156.0520, 10, 14, "night dive",
            "Night dive,Boat traffic,Do not touch the mantas",
            "Reef manta rays feeding on plankton,Blackjacks",
            "Boat from Kailua-Kona, Hawaii; kneel on the rubble zone, lights up.");
        Add("Molokini Back Wall", 20.6300, -156.4970, 10, 40, "wall",
            "Blue-water wall,Down-currents possible,Depth creep",
            "Whitetip reef sharks,Pelagics,Black coral",
            "Boat from Maui, Hawaii; drift along the crater's outer wall.");
        Add("Casino Point", 33.3490, -118.3220, 5, 29, "kelp forest",
            "Kelp entanglement,Boat traffic outside the park",
            "Giant sea bass,Garibaldi,Kelp bass,Horn sharks,Octopus",
            "Shore entry at Avalon, Catalina Island, California; dedicated dive park with stairs.");
        Add("Browning Wall", 50.8430, -127.6580, 5, 30, "wall",
            "Cold water,Current,Drysuit exposure",
            "Strawberry anemones,Wolf eels,Giant Pacific octopus,Rockfish",
            "Boat from Port Hardy, British Columbia; cold-water diving at its richest.");

        // Mexico, Galapagos & Eastern Pacific
        Add("Cenote Dos Ojos", 20.3240, -87.3920, 5, 10, "cavern",
            "Overhead environment,Follow the guideline,No fins-up silting",
            "Freshwater tetras,Stalactite and stalagmite formations,Light beams",
            "Near Tulum, Mexico; cavern-line dives with a guide; cave training required beyond the line.");
        Add("Cenote Angelita", 20.1370, -87.5780, 25, 40, "cenote",
            "Depth,Hydrogen-sulfide layer disorientation,Overhead feel below the cloud",
            "The surreal sulfide 'river' at ~30 m,Tree branches piercing the cloud",
            "Jungle cenote south of Tulum, Mexico; deep profile — advanced divers only.");
        Add("Palancar Gardens", 20.3320, -87.0290, 10, 25, "reef",
            "Drift diving,Boat traffic",
            "Splendid toadfish,Angelfish,Coral pinnacles and swim-throughs",
            "Boat from Cozumel, Mexico; classic gentle drift along the strand.");
        Add("Santa Rosa Wall", 20.4030, -87.0280, 15, 40, "wall",
            "Strong currents,Depth along the wall",
            "Eagle rays,Turtles,Grouper,Sponges and overhangs",
            "Boat from Cozumel, Mexico; faster drift, stay with the group.");
        Add("Cabo Pulmo Reef", 23.4380, -109.4250, 8, 18, "reef",
            "Surge close to shore,Park rules strictly enforced",
            "Massive jack tornadoes,Bull sharks,Grouper,Mobula rays",
            "Boat from Cabo Pulmo village, Mexico; national marine park, licensed operators only.");
        Add("Roca Partida", 18.9940, -112.0800, 15, 40, "pinnacle",
            "Open-ocean exposure,Current,No shelter",
            "Whitetip shark stacks on ledges,Giant mantas,Whale sharks,Tuna,Humpbacks in season",
            "Revillagigedo Archipelago, Mexico; liveaboard only, 24+ hours offshore.");
        Add("Darwin's Arch", 1.6780, -92.0000, 10, 30, "pinnacle",
            "Strong currents,Surge,Open-ocean exposure",
            "Whale sharks,Scalloped hammerhead schools,Galapagos sharks,Dolphins",
            "Darwin Island, Galapagos; liveaboard only; the arch collapsed in 2021 — the pillars remain.");
        Add("Gordon Rocks", -0.5650, -90.1410, 10, 32, "crater",
            "Strong currents,Surge in the washing machine",
            "Hammerheads,Galapagos sharks,Mola mola,Sea lions",
            "Day boat from Santa Cruz, Galapagos; advanced divers, current-dependent.");
        Add("Bajo Alcyone", 5.5200, -87.0700, 25, 35, "seamount",
            "Depth,Current,Remote open ocean",
            "Scalloped hammerhead schools,Marble rays,Silky sharks,Whale sharks",
            "Cocos Island, Costa Rica; liveaboard expeditions only, ~36 hours offshore.");

        // Caribbean & Atlantic
        Add("Great Blue Hole", 17.3160, -87.5350, 30, 40, "sinkhole",
            "Depth,Dark overhead feel under the rim,Short no-deco times",
            "Stalactites at depth,Caribbean reef sharks,Midnight parrotfish",
            "Lighthouse Reef, Belize; day trip or liveaboard; deep, brief, unforgettable.");
        Add("USS Oriskany", 30.0430, -87.0070, 24, 65, "wreck",
            "Depth,Current,Most of the wreck below recreational limits",
            "Amberjack,Barracuda,Snapper,Sand tiger sharks (occasional)",
            "Boat from Pensacola, Florida; the island tower starts deep — plan conservatively.");
        Add("Spiegel Grove", 25.0670, -80.3040, 14, 40, "wreck",
            "Current,Penetration hazards,Depth",
            "Goliath grouper,Barracuda,Horse-eye jacks,Corals colonizing the hull",
            "Boat from Key Largo, Florida; mooring buoys, check current before descent.");
        Add("Bloody Bay Wall", 19.7000, -80.0830, 6, 40, "wall",
            "Sheer vertical drop,Buoyancy discipline essential",
            "Sponges,Sea turtles,Groupers,Reef sharks",
            "Boat from Little Cayman; the wall starts astonishingly shallow.");
        Add("USS Kittiwake", 19.3620, -81.4000, 5, 20, "wreck",
            "Mild current,Other divers inside",
            "Horse-eye jacks,Barracuda,Turtles,Garden eels on the sand",
            "Boat from Seven Mile Beach, Grand Cayman; upright, open, beginner-friendly wreck.");
        Add("Tiger Beach", 26.8600, -79.0500, 6, 12, "shark dive",
            "Baited shark dive,Multiple large tiger sharks,Long bottom times in shallow water",
            "Tiger sharks,Lemon sharks,Nurse sharks,Great hammerheads in season",
            "Liveaboard or day boat from West End, Grand Bahama; strict briefing compliance required.");
        Add("1000 Steps", 12.2170, -68.3430, 5, 30, "reef",
            "Long stair carry,Surge at entry",
            "Hard coral terraces,Turtles,Parrotfish,Occasional eagle ray",
            "Shore dive on Bonaire's west coast; the 67 steps feel like 1000 with tanks.");
        Add("Salt Pier", 12.0840, -68.2860, 5, 18, "artificial",
            "Active pier — no diving when ships moored,Boat traffic",
            "Pillars wrapped in sponges and corals,Seahorses,Frogfish,Turtles",
            "Shore entry on Bonaire; dive only when the pier is clear, check locally.");

        // Indian Ocean & Southeast Asia
        Add("Richelieu Rock", 9.3600, 98.0220, 5, 35, "pinnacle",
            "Current,Open-water crossing,Seasonal closures",
            "Whale sharks,Barracuda vortex,Harlequin shrimp,Seahorses,Soft corals",
            "Liveaboard or speedboat from Khao Lak, Thailand; horseshoe pinnacle, best Feb–Apr.");
        Add("Chumphon Pinnacle", 10.1500, 99.7700, 14, 36, "pinnacle",
            "Depth,Thermoclines,Occasional strong current",
            "Whale sharks (seasonal),Giant grouper,Batfish,Anemone fields",
            "Boat from Koh Tao, Thailand; granite pinnacle northwest of the island.");
        Add("Koh Bon Ridge", 9.0990, 97.7940, 15, 35, "ridge",
            "Current on the ridge,Depth",
            "Manta rays,Leopard sharks,Napoleon wrasse",
            "Similan Islands liveaboard, Thailand; the ridge is the manta magnet.");
        Add("Barracuda Point Sipadan", 4.1150, 118.6280, 5, 40, "reef",
            "Strong currents,Daily permit quota,Wall drop-off",
            "Barracuda tornado,Bumphead parrotfish,Grey reef sharks,Turtles everywhere",
            "Boat from Mabul/Kapalai, Malaysia; Sipadan permits are limited — book ahead.");
        Add("USAT Liberty", -8.2740, 115.5930, 5, 30, "wreck",
            "Early crowds,Surge in shallow sections",
            "Bumphead parrotfish at dawn,Garden eels,Pygmy seahorses,Reef octopus",
            "Shore entry at Tulamben, Bali; arguably the world's easiest great wreck dive.");
        Add("Crystal Bay", -8.7160, 115.4580, 10, 40, "reef",
            "Cold thermoclines,Strong currents,Down-currents at the corner",
            "Mola mola (Jul–Oct),Reef sharks,Rich coral gardens",
            "Boat from Nusa Penida/Lembongan, Indonesia; treat the corners with respect.");
        Add("Manta Point Nusa Penida", -8.7960, 115.5410, 10, 25, "cleaning station",
            "Surge,Swell exposure on the south coast",
            "Reef manta rays,Blue-spotted rays,Bamboo sharks",
            "Boat from Nusa Penida, Indonesia; mantas circle the cleaning bommies.");
        Add("Cape Kri", -0.5560, 130.6910, 5, 35, "reef",
            "Current,Depth along the slope",
            "Record-setting fish diversity,Sweetlips,Jacks,Wobbegongs,Reef sharks",
            "House reef of Kri Island resorts, Raja Ampat, Indonesia.");
        Add("Monad Shoal", 11.3300, 124.1900, 15, 30, "seamount",
            "Dawn dive,Depth on the ledges,Respect distance rules",
            "Pelagic thresher sharks at cleaning stations,Manta rays (occasional)",
            "Dawn boat from Malapascua, Philippines; the world's most reliable thresher encounter.");
        Add("Irako Wreck", 11.9800, 120.0230, 28, 42, "wreck",
            "Depth,Penetration hazards,Current",
            "Schools of snapper,Grouper,Lionfish,Black coral",
            "Boat from Coron, Philippines; the deepest and most intact of the Coron fleet.");
        Add("Tubbataha North Atoll", 8.9500, 119.9900, 5, 40, "atoll",
            "Remote open sea,Current,Wall exposure",
            "Whitetip and grey reef sharks,Turtles,Jacks,Pristine coral cover",
            "Liveaboard from Puerto Princesa, Philippines; season runs mid-March to mid-June.");
        Add("Fish Head", 3.9550, 72.9150, 10, 30, "thila",
            "Current,Depth on the overhangs",
            "Grey reef sharks,Fusilier clouds,Napoleon wrasse,Overhangs with soft coral",
            "North Ari Atoll, Maldives; classic thila best dived on moderate current.");
        Add("Lankan Manta Point", 4.2900, 73.5600, 10, 25, "cleaning station",
            "Current,Snorkeler traffic above",
            "Reef manta rays (May–Nov),Turtles,Reef sharks",
            "Boat from North Male Atoll, Maldives; hold position off the cleaning blocks.");

        // Africa
        Add("Aliwal Shoal", -30.2500, 30.8200, 12, 30, "reef",
            "Surge,Surface swell on launch,Baited dives nearby",
            "Ragged-tooth sharks (Jun–Nov),Potato bass,Rays,Humpbacks in season",
            "Surf launch from Umkomaas, South Africa; conditions change fast.");
        Add("Protea Banks", -30.8330, 30.4830, 27, 40, "reef",
            "Depth,Strong currents,Advanced-only profile",
            "Bull sharks,Hammerheads,Tiger sharks (seasonal),Sardine-run action",
            "Surf launch from Shelly Beach, South Africa; deep baited and drift dives.");
        Add("Two Mile Reef", -27.5200, 32.6800, 9, 34, "reef",
            "Surge,Surf launch",
            "Turtles,Potato bass,Moorish idols,Whale sharks in season",
            "Launch from Sodwana Bay, South Africa; iSimangaliso Wetland Park rules apply.");
        Add("Manta Reef", -23.9300, 35.5300, 18, 30, "cleaning station",
            "Depth,Surge,Long boat rides",
            "Reef and giant mantas,Whale sharks nearby,Leopard sharks",
            "Boat from Tofo, Mozambique; manta cleaning stations on the deeper ledges.");

        return sites;
    }
}

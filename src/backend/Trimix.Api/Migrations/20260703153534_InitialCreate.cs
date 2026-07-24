using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Trimix.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AnalyticsEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserPseudoId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PropsJson = table.Column<string>(type: "text", nullable: false),
                    ClientTimestampUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Platform = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AppVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnalyticsEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Dives",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DurationSec = table.Column<int>(type: "integer", nullable: false),
                    MaxDepthM = table.Column<double>(type: "double precision", nullable: false),
                    AvgDepthM = table.Column<double>(type: "double precision", nullable: true),
                    MinTempC = table.Column<double>(type: "double precision", nullable: true),
                    GasMix = table.Column<string>(type: "text", nullable: true),
                    Source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SourceVendor = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: true),
                    ConditionsJson = table.Column<string>(type: "text", nullable: true),
                    GearCsv = table.Column<string>(type: "text", nullable: true),
                    BuddiesCsv = table.Column<string>(type: "text", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    Rating = table.Column<int>(type: "integer", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Dives", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SiteContributions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ModeratorId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResolvedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Note = table.Column<string>(type: "text", nullable: true),
                    SubmittedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteContributions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Sites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Lat = table.Column<double>(type: "double precision", nullable: false),
                    Lng = table.Column<double>(type: "double precision", nullable: false),
                    DepthMinM = table.Column<double>(type: "double precision", nullable: true),
                    DepthMaxM = table.Column<double>(type: "double precision", nullable: true),
                    Type = table.Column<string>(type: "text", nullable: true),
                    HazardsCsv = table.Column<string>(type: "text", nullable: true),
                    MarineLifeCsv = table.Column<string>(type: "text", nullable: true),
                    AccessNotes = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sites", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: false),
                    Role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Verified = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProfileSamples",
                columns: table => new
                {
                    DiveId = table.Column<Guid>(type: "uuid", nullable: false),
                    TSeconds = table.Column<int>(type: "integer", nullable: false),
                    DepthM = table.Column<double>(type: "double precision", nullable: false),
                    TempC = table.Column<double>(type: "double precision", nullable: true),
                    PpO2 = table.Column<double>(type: "double precision", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProfileSamples", x => new { x.DiveId, x.TSeconds });
                    table.ForeignKey(
                        name: "FK_ProfileSamples_Dives_DiveId",
                        column: x => x.DiveId,
                        principalTable: "Dives",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Entitlements",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tier = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SourceReceiptId = table.Column<Guid>(type: "uuid", nullable: true),
                    Platform = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    GrantedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Entitlements", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_Entitlements_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Receipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Platform = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SkuId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Token = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    FirstSeenUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Receipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Receipts_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Sites",
                columns: new[] { "Id", "AccessNotes", "DepthMaxM", "DepthMinM", "HazardsCsv", "Lat", "Lng", "MarineLifeCsv", "Name", "Source", "Status", "Type" },
                values: new object[,]
                {
                    { new Guid("0301ab46-c291-e9ac-dda9-88445414ee9f"), "Liveaboard or day boat from West End, Grand Bahama; strict briefing compliance required.", 12.0, 6.0, "Baited shark dive,Multiple large tiger sharks,Long bottom times in shallow water", 26.859999999999999, -79.049999999999997, "Tiger sharks,Lemon sharks,Nurse sharks,Great hammerheads in season", "Tiger Beach", "seed", "live", "shark dive" },
                    { new Guid("04a63178-abc6-a182-9222-3669bf517370"), "Boat from Pensacola, Florida; the island tower starts deep — plan conservatively.", 65.0, 24.0, "Depth,Current,Most of the wreck below recreational limits", 30.042999999999999, -87.007000000000005, "Amberjack,Barracuda,Snapper,Sand tiger sharks (occasional)", "USS Oriskany", "seed", "live", "wreck" },
                    { new Guid("0d8b6175-9f1a-6081-89be-4672ba29acee"), "Chuuk (Truk) Lagoon; boat from Weno, guided wreck protocols apply.", 34.0, 9.0, "Penetration hazards,Silt-out risk inside holds", 7.4458000000000002, 151.80099999999999, "Zero fighter remains in holds,Soft corals,Glassfish,Anemonefish", "Fujikawa Maru", "seed", "live", "wreck" },
                    { new Guid("0f8d48f6-1d54-fbde-08dd-a7c727a75bc7"), "Boat from Kailua-Kona, Hawaii; kneel on the rubble zone, lights up.", 14.0, 10.0, "Night dive,Boat traffic,Do not touch the mantas", 19.736999999999998, -156.05199999999999, "Reef manta rays feeding on plankton,Blackjacks", "Manta Ray Night Dive Kona", "seed", "live", "night dive" },
                    { new Guid("1755995a-8545-e411-11cb-9cb4f492db19"), "Boat from Tutukaka to the Poor Knights Islands, New Zealand; marine reserve.", 24.0, 5.0, "Surge through the arch", -35.469999999999999, 174.73599999999999, "Blue maomao schools,Stingrays,Kelp forest life", "Blue Maomao Arch", "seed", "live", "arch" },
                    { new Guid("1e9e1430-291d-bf3b-9acc-8c17be70110e"), "Day boat or liveaboard from Stromness, Scapa Flow, Orkney; advanced/tec profile.", 45.0, 24.0, "Depth,Cold dark water,Protected war grave — no penetration", 58.890000000000001, -3.1549999999999998, "Wrasse,Pollack,Anemones,Dead man's fingers", "SMS Markgraf", "seed", "live", "wreck" },
                    { new Guid("245f31ec-d436-61c7-95dd-9b2fb108c75c"), "Boat from Cozumel, Mexico; faster drift, stay with the group.", 40.0, 15.0, "Strong currents,Depth along the wall", 20.402999999999999, -87.028000000000006, "Eagle rays,Turtles,Grouper,Sponges and overhangs", "Santa Rosa Wall", "seed", "live", "wall" },
                    { new Guid("2abfa641-035d-3014-eda1-139cb0274244"), "Boat from Taveuni, Fiji; timed to slack tide on the Rainbow Reef.", 35.0, 10.0, "Strong tidal currents in Somosomo Strait", -16.789999999999999, -179.94999999999999, "White soft corals,Anthias,Reef sharks,Barracuda", "Great White Wall", "seed", "live", "wall" },
                    { new Guid("2c02dd9c-35cc-de13-dba9-0de56f8b8b61"), "Boat from Sharm El Sheikh or liveaboard; advanced certification recommended; early start beats crowds.", 32.0, 16.0, "Strong currents,Boat traffic,Penetration hazards", 27.813300000000002, 33.920299999999997, "Batfish,Barracuda,Trevally,Crocodilefish,Stonefish", "SS Thistlegorm", "seed", "live", "wreck" },
                    { new Guid("310f73b2-118f-7247-d00c-4e0507505b48"), "Chuuk (Truk) Lagoon; boat from Weno; one of the lagoon's most coral-rich wrecks.", 38.0, 12.0, "Penetration hazards,Depth on the engine room", 7.399, 151.75, "Soft coral gardens on masts,Operating theatre artifacts,Batfish", "Shinkoku Maru", "seed", "live", "wreck" },
                    { new Guid("3b181d5d-cc1c-1a8d-24ba-f777e9bc96f9"), "Short boat ride from Larnaca, Cyprus; one of the world's top wreck dives.", 42.0, 16.0, "Penetration hazards,Disorientation inside cargo decks,Depth", 34.901600000000002, 33.653500000000001, "Grouper,Barracuda,Jacks,Turtles", "Zenobia Wreck", "seed", "live", "wreck" },
                    { new Guid("442418e5-5f6c-8d5e-48c7-60556d99397b"), "Shore entry at Tulamben, Bali; arguably the world's easiest great wreck dive.", 30.0, 5.0, "Early crowds,Surge in shallow sections", -8.2739999999999991, 115.593, "Bumphead parrotfish at dawn,Garden eels,Pygmy seahorses,Reef octopus", "USAT Liberty", "seed", "live", "wreck" },
                    { new Guid("46c9660f-3d85-8275-3d86-f6a900bdd4c4"), "Boat from Nusa Penida, Indonesia; mantas circle the cleaning bommies.", 25.0, 10.0, "Surge,Swell exposure on the south coast", -8.7959999999999994, 115.541, "Reef manta rays,Blue-spotted rays,Bamboo sharks", "Manta Point Nusa Penida", "seed", "live", "cleaning station" },
                    { new Guid("4a83a4c7-2f78-4afa-afd4-260c4d3ae191"), "Exmouth, Western Australia; access only via the licensed operator, ID required.", 15.0, 8.0, "Restricted naval facility,Low visibility on big tides", -21.815999999999999, 114.18300000000001, "Wobbegongs,Grey nurse sharks,Frogfish,Nudibranchs,Octopus", "Navy Pier", "seed", "live", "artificial" },
                    { new Guid("4fde9203-8f2d-d72d-26fc-4bdca3bb9dff"), "Near Tulum, Mexico; cavern-line dives with a guide; cave training required beyond the line.", 10.0, 5.0, "Overhead environment,Follow the guideline,No fins-up silting", 20.324000000000002, -87.391999999999996, "Freshwater tetras,Stalactite and stalagmite formations,Light beams", "Cenote Dos Ojos", "seed", "live", "cavern" },
                    { new Guid("5253400c-8e25-91e3-0874-93b8a630c312"), "Osprey Reef, Coral Sea; liveaboard only.", 40.0, 10.0, "Strong currents,Blue-water exposure,Shark feed dives", -13.808999999999999, 146.554, "Grey reef sharks,Silvertips,Hammerheads,Dogtooth tuna", "North Horn", "seed", "live", "wall" },
                    { new Guid("57796fd3-bb92-0e4a-8e03-5b7d71754f36"), "Shore entry on Bonaire; dive only when the pier is clear, check locally.", 18.0, 5.0, "Active pier — no diving when ships moored,Boat traffic", 12.084, -68.286000000000001, "Pillars wrapped in sponges and corals,Seahorses,Frogfish,Turtles", "Salt Pier", "seed", "live", "artificial" },
                    { new Guid("58185d15-2c38-ef9e-a356-e12870322e86"), "Surf launch from Umkomaas, South Africa; conditions change fast.", 30.0, 12.0, "Surge,Surface swell on launch,Baited dives nearby", -30.25, 30.82, "Ragged-tooth sharks (Jun–Nov),Potato bass,Rays,Humpbacks in season", "Aliwal Shoal", "seed", "live", "reef" },
                    { new Guid("588492d3-3950-7a29-341f-a60948dbcc20"), "Zodiac or liveaboard from Marsa Alam; advanced divers only in season.", 40.0, 12.0, "Strong currents,Blue-water exposure,Oceanic whitetip encounters", 25.312899999999999, 34.8598, "Oceanic whitetip sharks,Hammerhead sharks,Soft corals,Anthias", "Elphinstone Reef", "seed", "live", "wall" },
                    { new Guid("5cb10c76-9790-2e9e-4f76-4f80896a170c"), "Shore entry at Avalon, Catalina Island, California; dedicated dive park with stairs.", 29.0, 5.0, "Kelp entanglement,Boat traffic outside the park", 33.348999999999997, -118.322, "Giant sea bass,Garibaldi,Kelp bass,Horn sharks,Octopus", "Casino Point", "seed", "live", "kelp forest" },
                    { new Guid("5cde105b-38dc-06de-08e2-9c2ff6259edd"), "Boat from Koror, Palau; hook in at the plateau edge and watch the parade.", 30.0, 8.0, "Strong unpredictable currents,Reef hook required", 7.1349999999999998, 134.22, "Grey reef sharks,Napoleon wrasse,Barracuda tornado,Eagle rays,Jacks", "Blue Corner", "seed", "live", "wall" },
                    { new Guid("5d7d562b-6568-bfe2-9bd6-5013b380d3c2"), "Boat from Coron, Philippines; the deepest and most intact of the Coron fleet.", 42.0, 28.0, "Depth,Penetration hazards,Current", 11.98, 120.023, "Schools of snapper,Grouper,Lionfish,Black coral", "Irako Wreck", "seed", "live", "wreck" },
                    { new Guid("5f92dd24-c31a-79d3-57c9-8466023d110e"), "Liveaboard from Puerto Princesa, Philippines; season runs mid-March to mid-June.", 40.0, 5.0, "Remote open sea,Current,Wall exposure", 8.9499999999999993, 119.98999999999999, "Whitetip and grey reef sharks,Turtles,Jacks,Pristine coral cover", "Tubbataha North Atoll", "seed", "live", "atoll" },
                    { new Guid("6237fa0f-0b6d-0340-0461-3889918a4a67"), "Similan Islands liveaboard, Thailand; the ridge is the manta magnet.", 35.0, 15.0, "Current on the ridge,Depth", 9.0990000000000002, 97.793999999999997, "Manta rays,Leopard sharks,Napoleon wrasse", "Koh Bon Ridge", "seed", "live", "ridge" },
                    { new Guid("6530eee4-c807-e32e-5426-20821804e189"), "Boat from Sharm El Sheikh (Ras Mohammed National Park); usually dived as a drift.", 40.0, 10.0, "Strong currents,Down-currents at the corner", 27.723099999999999, 34.250599999999999, "Grey reef sharks,Napoleon wrasse,Snapper schools,Barracuda,Batfish", "Shark and Yolanda Reef", "seed", "live", "wall" },
                    { new Guid("6c59dca5-a57c-2847-92c1-2b700876a43a"), "Shore entry near Luganville, Espiritu Santo, Vanuatu; deep sections are technical dives.", 70.0, 20.0, "Depth,Penetration hazards,Deco obligations on deeper sections", -15.523999999999999, 167.185, "Lionfish,Barracuda,Moray eels,The Lady statue,Coral encrusted holds", "SS President Coolidge", "seed", "live", "wreck" },
                    { new Guid("6c98a00e-aa04-0559-9084-d698d1cf9571"), "Launch from Sodwana Bay, South Africa; iSimangaliso Wetland Park rules apply.", 34.0, 9.0, "Surge,Surf launch", -27.52, 32.68, "Turtles,Potato bass,Moorish idols,Whale sharks in season", "Two Mile Reef", "seed", "live", "reef" },
                    { new Guid("710ded55-240a-48af-08ea-73397b256ddc"), "Darwin Island, Galapagos; liveaboard only; the arch collapsed in 2021 — the pillars remain.", 30.0, 10.0, "Strong currents,Surge,Open-ocean exposure", 1.6779999999999999, -92.0, "Whale sharks,Scalloped hammerhead schools,Galapagos sharks,Dolphins", "Darwin's Arch", "seed", "live", "pinnacle" },
                    { new Guid("7786650a-e46f-57f7-72f7-093551398d36"), "Liveaboard or speedboat from Khao Lak, Thailand; horseshoe pinnacle, best Feb–Apr.", 35.0, 5.0, "Current,Open-water crossing,Seasonal closures", 9.3599999999999994, 98.022000000000006, "Whale sharks,Barracuda vortex,Harlequin shrimp,Seahorses,Soft corals", "Richelieu Rock", "seed", "live", "pinnacle" },
                    { new Guid("7fcfbac7-be63-ff84-e5fd-70f75f539bbd"), "Dawn boat from Malapascua, Philippines; the world's most reliable thresher encounter.", 30.0, 15.0, "Dawn dive,Depth on the ledges,Respect distance rules", 11.33, 124.19, "Pelagic thresher sharks at cleaning stations,Manta rays (occasional)", "Monad Shoal", "seed", "live", "seamount" },
                    { new Guid("8388797b-5e51-9ebb-994e-748d7d39ec8f"), "Boat from Little Cayman; the wall starts astonishingly shallow.", 40.0, 6.0, "Sheer vertical drop,Buoyancy discipline essential", 19.699999999999999, -80.082999999999998, "Sponges,Sea turtles,Groupers,Reef sharks", "Bloody Bay Wall", "seed", "live", "wall" },
                    { new Guid("944a083f-4991-dc05-dbfb-4046e87b711f"), "Boat from Yonaguni Island, Japan; best conditions November–June.", 25.0, 5.0, "Strong currents,Surge near the terraces", 24.436, 122.938, "Hammerhead sharks in winter,The carved-looking megalith terraces", "Yonaguni Monument", "seed", "live", "rock formation" },
                    { new Guid("96f47f13-f5c7-1e0c-a9a2-03fb4fc7525f"), "Boat from Koh Tao, Thailand; granite pinnacle northwest of the island.", 36.0, 14.0, "Depth,Thermoclines,Occasional strong current", 10.15, 99.769999999999996, "Whale sharks (seasonal),Giant grouper,Batfish,Anemone fields", "Chumphon Pinnacle", "seed", "live", "pinnacle" },
                    { new Guid("9af9ba8f-3e23-7236-8206-ee031709c140"), "Day boat from Santa Cruz, Galapagos; advanced divers, current-dependent.", 32.0, 10.0, "Strong currents,Surge in the washing machine", -0.56499999999999995, -90.141000000000005, "Hammerheads,Galapagos sharks,Mola mola,Sea lions", "Gordon Rocks", "seed", "live", "crater" },
                    { new Guid("9c8da3af-9229-b7ba-cac2-34aa8f769726"), "Boat from Koror, Palau; timed around manta cleaning-station activity.", 25.0, 12.0, "Boat traffic in the channel,Current at the cleaning stations", 7.1180000000000003, 134.256, "Manta rays,Grey reef sharks,Garden eels,Snapper schools", "German Channel", "seed", "live", "channel" },
                    { new Guid("9d3a7118-8f23-08f7-c788-67027770e4bd"), "Sudan liveaboard only; plateau at 9–30 m with shark action at the drop-off.", 30.0, 9.0, "Currents on the plateau edge,Remote location", 19.939, 37.412999999999997, "Grey reef sharks,Bumphead parrotfish,Jacks,Remains of Cousteau's Conshelf II", "Sha'ab Rumi South Plateau", "seed", "live", "reef" },
                    { new Guid("a1bd4a7d-b7b5-e8a7-2512-508bdff0d00a"), "Cocos Island, Costa Rica; liveaboard expeditions only, ~36 hours offshore.", 35.0, 25.0, "Depth,Current,Remote open ocean", 5.5199999999999996, -87.069999999999993, "Scalloped hammerhead schools,Marble rays,Silky sharks,Whale sharks", "Bajo Alcyone", "seed", "live", "seamount" },
                    { new Guid("a2d977d5-a481-1533-29f6-38011eadad57"), "Lighthouse Reef, Belize; day trip or liveaboard; deep, brief, unforgettable.", 40.0, 30.0, "Depth,Dark overhead feel under the rim,Short no-deco times", 17.315999999999999, -87.534999999999997, "Stalactites at depth,Caribbean reef sharks,Midnight parrotfish", "Great Blue Hole", "seed", "live", "sinkhole" },
                    { new Guid("a77ee897-e614-ab56-928c-d94267798687"), "Shore entry at Wied iz-Zurrieq, Malta; long swim — plan gas accordingly.", 36.0, 15.0, "Depth,Occasional surge,Penetration hazards", 35.819000000000003, 14.452, "Barracuda,Amberjack,Scorpionfish,Moray eels", "Um El Faroud", "seed", "live", "wreck" },
                    { new Guid("aa3a7113-6292-0569-5b88-b6296c71740a"), "Boat from Maui, Hawaii; drift along the crater's outer wall.", 40.0, 10.0, "Blue-water wall,Down-currents possible,Depth creep", 20.629999999999999, -156.49700000000001, "Whitetip reef sharks,Pelagics,Black coral", "Molokini Back Wall", "seed", "live", "wall" },
                    { new Guid("af099550-e427-b0b4-a564-a097c5da8f18"), "Boat from Nusa Penida/Lembongan, Indonesia; treat the corners with respect.", 40.0, 10.0, "Cold thermoclines,Strong currents,Down-currents at the corner", -8.7159999999999993, 115.458, "Mola mola (Jul–Oct),Reef sharks,Rich coral gardens", "Crystal Bay", "seed", "live", "reef" },
                    { new Guid("b0754f66-b56e-f866-6ec0-9b3a13ea60a2"), "Boat from Mabul/Kapalai, Malaysia; Sipadan permits are limited — book ahead.", 40.0, 5.0, "Strong currents,Daily permit quota,Wall drop-off", 4.1150000000000002, 118.628, "Barracuda tornado,Bumphead parrotfish,Grey reef sharks,Turtles everywhere", "Barracuda Point Sipadan", "seed", "live", "reef" },
                    { new Guid("b3c0f1ff-04dc-a83e-78a1-efaf0a6505d3"), "Ribbon Reefs, Great Barrier Reef; liveaboard from Cairns.", 25.0, 10.0, "Mild currents", -14.66, 145.66, "Potato cod,Coral trout,Sweetlips,Reef sharks", "Cod Hole", "seed", "live", "reef" },
                    { new Guid("b74d78d7-7586-4acd-1456-1552b18d1421"), "Revillagigedo Archipelago, Mexico; liveaboard only, 24+ hours offshore.", 40.0, 15.0, "Open-ocean exposure,Current,No shelter", 18.994, -112.08, "Whitetip shark stacks on ledges,Giant mantas,Whale sharks,Tuna,Humpbacks in season", "Roca Partida", "seed", "live", "pinnacle" },
                    { new Guid("b890756e-29ec-e340-d220-d68dd8b2f28d"), "Surf launch from Shelly Beach, South Africa; deep baited and drift dives.", 40.0, 27.0, "Depth,Strong currents,Advanced-only profile", -30.832999999999998, 30.483000000000001, "Bull sharks,Hammerheads,Tiger sharks (seasonal),Sardine-run action", "Protea Banks", "seed", "live", "reef" },
                    { new Guid("bbee37f4-1993-c505-9f23-db0c5cb38dda"), "Boat from Tofo, Mozambique; manta cleaning stations on the deeper ledges.", 30.0, 18.0, "Depth,Surge,Long boat rides", -23.93, 35.530000000000001, "Reef and giant mantas,Whale sharks nearby,Leopard sharks", "Manta Reef", "seed", "live", "cleaning station" },
                    { new Guid("bd514319-5e63-81fb-b32e-1a89e0e13b23"), "House reef of Kri Island resorts, Raja Ampat, Indonesia.", 35.0, 5.0, "Current,Depth along the slope", -0.55600000000000005, 130.691, "Record-setting fish diversity,Sweetlips,Jacks,Wobbegongs,Reef sharks", "Cape Kri", "seed", "live", "reef" },
                    { new Guid("c053bad7-c9e0-af58-66ca-65d32bb8c73c"), "Jungle cenote south of Tulum, Mexico; deep profile — advanced divers only.", 40.0, 25.0, "Depth,Hydrogen-sulfide layer disorientation,Overhead feel below the cloud", 20.137, -87.578000000000003, "The surreal sulfide 'river' at ~30 m,Tree branches piercing the cloud", "Cenote Angelita", "seed", "live", "cenote" },
                    { new Guid("c3f65b30-bc55-2bb4-6fba-01896be86fbf"), "Boat from Seven Mile Beach, Grand Cayman; upright, open, beginner-friendly wreck.", 20.0, 5.0, "Mild current,Other divers inside", 19.361999999999998, -81.400000000000006, "Horse-eye jacks,Barracuda,Turtles,Garden eels on the sand", "USS Kittiwake", "seed", "live", "wreck" },
                    { new Guid("c85c4d6f-98aa-d652-4173-a97a0bcaf0bb"), "Bikini Atoll, Marshall Islands; technical liveaboard expeditions only.", 52.0, 30.0, "Technical depths,Remote location,Deco diving only", 11.587999999999999, 165.42699999999999, "Aircraft in hangars,Sharks patrolling the flight deck,Big pelagics", "USS Saratoga", "seed", "live", "wreck" },
                    { new Guid("cbfda9cc-305d-e693-b250-d4252d2bd4d9"), "Wingate Reef off Port Sudan; sheltered inside the reef, liveaboard access.", 36.0, 5.0, "Penetration hazards,Live WWII ordnance in holds", 19.622, 37.238, "Glassfish,Batfish,Hard coral growth on hull", "Umbria Wreck", "seed", "live", "wreck" },
                    { new Guid("cedec780-4550-705d-49b3-cc70779e5471"), "Boat from Port Hardy, British Columbia; cold-water diving at its richest.", 30.0, 5.0, "Cold water,Current,Drysuit exposure", 50.843000000000004, -127.658, "Strawberry anemones,Wolf eels,Giant Pacific octopus,Rockfish", "Browning Wall", "seed", "live", "wall" },
                    { new Guid("d5fdf3f4-3804-f74e-c834-4964080c564d"), "Shore entry over limestone at Dwejra, Gozo; rough seas make entry/exit hazardous.", 40.0, 8.0, "Surge in the chimney,Overhead at the arch", 36.053699999999999, 14.1883, "Octopus,Parrotfish,Moray eels,Nudibranchs", "Blue Hole Gozo", "seed", "live", "arch" },
                    { new Guid("daf96afd-eb8d-4705-003e-8a93dc7a6e75"), "Boat from Cozumel, Mexico; classic gentle drift along the strand.", 25.0, 10.0, "Drift diving,Boat traffic", 20.332000000000001, -87.028999999999996, "Splendid toadfish,Angelfish,Coral pinnacles and swim-throughs", "Palancar Gardens", "seed", "live", "reef" },
                    { new Guid("dc86d5d7-48d4-dddc-8436-00c2b2fd2502"), "Thingvellir National Park, Iceland; guided dives only, drysuit certification required.", 18.0, 7.0, "2–4°C water,Drysuit exposure,Site current toward the lagoon", 64.255899999999997, -21.116, "Bright green troll hair algae,Occasional Arctic char", "Silfra Fissure", "seed", "live", "fissure" },
                    { new Guid("df871a68-c8df-4ffc-1814-d9d9969b5630"), "Boat from North Male Atoll, Maldives; hold position off the cleaning blocks.", 25.0, 10.0, "Current,Snorkeler traffic above", 4.29, 73.560000000000002, "Reef manta rays (May–Nov),Turtles,Reef sharks", "Lankan Manta Point", "seed", "live", "cleaning station" },
                    { new Guid("e7a62fd1-99f2-68e8-02d9-e740c684c379"), "Boat from Matauri Bay, Cavalli Islands, New Zealand.", 26.0, 18.0, "Surge,Visibility varies", -34.979999999999997, 173.93700000000001, "Anemones covering the hull,John Dory,Kingfish,Moray eels", "Rainbow Warrior Wreck", "seed", "live", "wreck" },
                    { new Guid("ee9691d4-83b3-ae49-67da-8e746534788b"), "Shore dive on Bonaire's west coast; the 67 steps feel like 1000 with tanks.", 30.0, 5.0, "Long stair carry,Surge at entry", 12.217000000000001, -68.343000000000004, "Hard coral terraces,Turtles,Parrotfish,Occasional eagle ray", "1000 Steps", "seed", "live", "reef" },
                    { new Guid("f4219284-b301-77f6-7429-fe4c55c713ce"), "Boat from Key Largo, Florida; mooring buoys, check current before descent.", 40.0, 14.0, "Current,Penetration hazards,Depth", 25.067, -80.304000000000002, "Goliath grouper,Barracuda,Horse-eye jacks,Corals colonizing the hull", "Spiegel Grove", "seed", "live", "wreck" },
                    { new Guid("f56d7326-fe95-18ba-2500-6c964688a1ba"), "Boat from Pacific Harbour, Fiji; structured feed dive with strict positioning rules.", 30.0, 15.0, "Baited shark dive,Multiple large bull sharks", -18.390000000000001, 178.13, "Bull sharks,Tiger sharks (occasional),Giant trevally,Remoras", "Beqa Lagoon Shark Arena", "seed", "live", "shark dive" },
                    { new Guid("fa5b53f0-213c-e970-e9c2-198725a241bb"), "Boat from Cabo Pulmo village, Mexico; national marine park, licensed operators only.", 18.0, 8.0, "Surge close to shore,Park rules strictly enforced", 23.437999999999999, -109.425, "Massive jack tornadoes,Bull sharks,Grouper,Mobula rays", "Cabo Pulmo Reef", "seed", "live", "reef" },
                    { new Guid("fbfdae69-351f-1649-0a43-82ed7d0a3939"), "Shore entry via El Bells or the lagoon saddle; stay strictly within your certification limits.", 40.0, 7.0, "Depth beyond recreational limits,The Arch overhead at ~56 m,Peer-pressure depth incidents", 28.571999999999999, 34.537199999999999, "Fusiliers,Cornetfish,Triggerfish,Unicornfish", "Blue Hole Dahab", "seed", "live", "sinkhole" },
                    { new Guid("fd000a5c-2298-9dd7-3d80-e7e52dfec030"), "North Ari Atoll, Maldives; classic thila best dived on moderate current.", 30.0, 10.0, "Current,Depth on the overhangs", 3.9550000000000001, 72.915000000000006, "Grey reef sharks,Fusilier clouds,Napoleon wrasse,Overhangs with soft coral", "Fish Head", "seed", "live", "thila" },
                    { new Guid("ff18ccbd-776f-e8cb-1ba5-2e424d41748b"), "Boat from Ayr or Townsville, Queensland; advanced certification recommended.", 28.0, 14.0, "Strong currents,No penetration (protected historic wreck),Exposed open-water site", -19.305, 147.62100000000001, "Giant grouper,Bull rays,Sea snakes,Turtles,Trevally,Eagle rays", "SS Yongala", "seed", "live", "wreck" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_AnalyticsEvents_UserPseudoId_Id",
                table: "AnalyticsEvents",
                columns: new[] { "UserPseudoId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Dives_UserId_UpdatedAtUtc",
                table: "Dives",
                columns: new[] { "UserId", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Receipts_Token",
                table: "Receipts",
                column: "Token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Receipts_UserId",
                table: "Receipts",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_SiteContributions_Status",
                table: "SiteContributions",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Sites_Lat_Lng",
                table: "Sites",
                columns: new[] { "Lat", "Lng" });

            migrationBuilder.CreateIndex(
                name: "IX_Sites_Name",
                table: "Sites",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AnalyticsEvents");

            migrationBuilder.DropTable(
                name: "Entitlements");

            migrationBuilder.DropTable(
                name: "ProfileSamples");

            migrationBuilder.DropTable(
                name: "Receipts");

            migrationBuilder.DropTable(
                name: "SiteContributions");

            migrationBuilder.DropTable(
                name: "Sites");

            migrationBuilder.DropTable(
                name: "Dives");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}

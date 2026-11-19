// Wygenerowane przez narzędzie (scratchpad gen_rfemcom.py) z biblioteki typów „Dlubal RFEM Type Library v5.7” (RFEM 5.27),
// zrzut kolejności metod zrobiony comtypes. RF-COM to interfejsy vtable po IUnknown (bez IDispatch), więc późne wiązanie
// (Type.InvokeMember jak w Office) nie działa — każdy interfejs jest tu zadeklarowany z dokładną liczbą miejsc w vtable.
// Metody, których Amadeusz używa, mają pełne sygnatury; pozostałe to zaślepki „_NNN_Nazwa” (nigdy nie wołane).
// Zgodność układu z zarejestrowaną biblioteką sprawdza RfemCom.VerifyLayout (po nazwach metod w ITypeInfo).
// Zmieniając plik ręcznie, nie przestawiaj kolejności i nie usuwaj zaślepek.
using System;
using System.Runtime.InteropServices;

#pragma warning disable CS1591 // zaślepki i pola struktur dokumentuje nagłówek pliku

namespace LokalneAI.Harness.Rfem.Com
{
    /// <summary>Aplikacja RFEM 5 (ProgID <c>RFEM5.Application</c>): licencja RF-COM i otwarte modele.</summary>
    [ComImport, Guid("A0F0224A-34DE-44E2-8F0D-F078BD89665F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IApplication
    {
        /// <summary>Miejsce 0: LockLicense().</summary>
        void LockLicense();
        /// <summary>Miejsce 1: UnlockLicense().</summary>
        void UnlockLicense();
        /// <summary>Miejsce 2: IsComLicensed(out *VARIANT_BOOL Licensed).</summary>
        [return: MarshalAs(UnmanagedType.VariantBool)] bool IsComLicensed();
        /// <summary>Miejsce 3: IsAppLicensed(out *VARIANT_BOOL Licensed).</summary>
        [return: MarshalAs(UnmanagedType.VariantBool)] bool IsAppLicensed();
        void _004_CreateModel();
        void _005_OpenModel();
        /// <summary>Miejsce 6: GetModelCount(out *c_long Count).</summary>
        int GetModelCount();
        /// <summary>Miejsce 7: GetModel(c_long Index, out *POINTER(IModel) Model).</summary>
        IModel GetModel(int index);
        /// <summary>Miejsce 8: GetActiveModel(out *POINTER(IModel) Model).</summary>
        IModel GetActiveModel();
        void _009_Show();
        void _010_Close();
        void _011_RefreshGUI();
        /// <summary>Miejsce 12: GetVersion(out *BSTR Version).</summary>
        [return: MarshalAs(UnmanagedType.BStr)] string GetVersion();
        /// <summary>Miejsce 13: GetPath(out *BSTR Path).</summary>
        [return: MarshalAs(UnmanagedType.BStr)] string GetPath();
        /// <summary>Miejsce 14: GetLanguage(out *BSTR Language).</summary>
        [return: MarshalAs(UnmanagedType.BStr)] string GetLanguage();
        void _015_SetLanguage();
        void _016_SetStatusBarText();
    }

    /// <summary>Otwarty model: nazwa, ścieżka, dane modelu, obciążenia, obliczenia, widok.</summary>
    [ComImport, Guid("71A4A82B-B939-4E6F-AF26-A9FD8FA5195C"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IModel
    {
        void _000_GetApplication();
        /// <summary>Miejsce 1: GetName(out *BSTR Name).</summary>
        [return: MarshalAs(UnmanagedType.BStr)] string GetName();
        void _002_SetName();
        /// <summary>Miejsce 3: GetDescription(out *BSTR Description).</summary>
        [return: MarshalAs(UnmanagedType.BStr)] string GetDescription();
        void _004_SetDescription();
        void _005_GetComment();
        void _006_SetComment();
        void _007_GetID();
        void _008_SetID();
        /// <summary>Miejsce 9: GetPath(out *BSTR Path).</summary>
        [return: MarshalAs(UnmanagedType.BStr)] string GetPath();
        void _010_GetTempFolderPath();
        void _011_Save();
        void _012_Close();
        void _013_Activate();
        void _014_SetModified();
        void _015_Clean();
        /// <summary>Miejsce 16: GetProjectName(out *BSTR Name).</summary>
        [return: MarshalAs(UnmanagedType.BStr)] string GetProjectName();
        void _017_GetProjectDescription();
        void _018_GetProjectPath();
        void _019_GetGlobalZAxisOrientation();
        void _020_SetGlobalZAxisOrientation();
        void _021_GetLocalZAxisOrientation();
        void _022_SetLocalZAxisOrientation();
        /// <summary>Miejsce 23: GetModelData(out *POINTER(IModelData) Data).</summary>
        IModelData GetModelData();
        /// <summary>Miejsce 24: GetLoads(out *POINTER(ILoads) Loads).</summary>
        ILoads GetLoads();
        /// <summary>Miejsce 25: GetCalculation(out *POINTER(ICalculation) Calculation).</summary>
        ICalculation GetCalculation();
        void _026_GetGuideObjects();
        /// <summary>Miejsce 27: GetActiveView(out *POINTER(IView) View).</summary>
        IView GetActiveView();
        void _028_GetModule();
        void _029_GetWorkPlane();
        void _030_SetWorkPlane();
        void _031_GetGrid();
        void _032_SetGrid();
        void _033_GetProtocols();
    }

    /// <summary>Dane modelu: węzły, linie, pręty, przekroje, materiały, przeguby, zbiory prętów (241 miejsc w vtable).</summary>
    [ComImport, Guid("FA497B7C-D217-477F-AF54-DFB262251DEB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IModelData
    {
        void _000_GetType();
        void _001_SetType();
        void _002_PrepareModification();
        void _003_FinishModification();
        void _004_CancelModification();
        void _005_IgnoreErrors();
        void _006_EnableSelections();
        void _007_SelectObjects();
        void _008_ConnectMembers();
        void _009_ConnectLines();
        void _010_ConnectAll();
        void _011_MoveSelectedObjects();
        void _012_CopySelectedObjects();
        void _013_RotateSelectedObjects();
        void _014_MirrorSelectedObjects();
        void _015_ProjectSelectedObjects();
        void _016_ScaleSelectedObjects();
        void _017_ShearSelectedObjects();
        void _018_GetLastObjectNo();
        void _019_Clean();
        void _020_DeleteObjects();
        void _021_SetObjectID();
        void _022_PlausibilityCheck();
        void _023_GetIdenticalNodes();
        void _024_CorrectIdenticalNodes();
        void _025_GetMaterialDatabase();
        void _026_GetCrossSectionDatabase();
        /// <summary>Miejsce 27: GetNodeCount(out *c_long Count).</summary>
        int GetNodeCount();
        /// <summary>Miejsce 28: GetNode(c_long No, c_long At, out *POINTER(INode) Node).</summary>
        INode GetNode(int no, int at);
        void _029_SetNode();
        void _030_GetNodes();
        void _031_SetNodes();
        /// <summary>Miejsce 32: GetLineCount(out *c_long Count).</summary>
        int GetLineCount();
        /// <summary>Miejsce 33: GetLine(c_long No, c_long At, out *POINTER(ILine) Line).</summary>
        ILine GetLine(int no, int at);
        void _034_SetLine();
        void _035_GetLines();
        void _036_SetLines();
        void _037_GetTrajectoryLineCount();
        void _038_GetTrajectoryLine();
        void _039_SetTrajectoryLine();
        void _040_GetTrajectoryLines();
        void _041_SetTrajectoryLines();
        void _042_GetLineOnSurfaceCount();
        void _043_GetLineOnSurface();
        void _044_SetLineOnSurface();
        void _045_GetLinesOnSurface();
        void _046_SetLinesOnSurface();
        void _047_GetParabolaCount();
        void _048_GetParabola();
        void _049_SetParabola();
        void _050_GetParabolas();
        void _051_SetParabolas();
        void _052_GetNurbSplineCount();
        void _053_GetNurbSpline();
        void _054_SetNurbSpline();
        void _055_GetNurbSplines();
        void _056_SetNurbSplines();
        /// <summary>Miejsce 57: GetMaterialCount(out *c_long Count).</summary>
        int GetMaterialCount();
        /// <summary>Miejsce 58: GetMaterial(c_long No, c_long At, out *POINTER(IMaterial) Material).</summary>
        IMaterial GetMaterial(int no, int at);
        void _059_SetMaterial();
        void _060_GetMaterials();
        void _061_SetMaterials();
        /// <summary>Miejsce 62: GetSurfaceCount(out *c_long Count).</summary>
        int GetSurfaceCount();
        void _063_GetSurface();
        void _064_SetSurface();
        void _065_GetSurfaces();
        void _066_SetSurfaces();
        void _067_GetPlaneSurfaceCount();
        void _068_GetPlaneSurface();
        void _069_SetPlaneSurface();
        void _070_GetPlaneSurfaces();
        void _071_SetPlaneSurfaces();
        void _072_GetQuadrangleSurfaceCount();
        void _073_GetQuadrangleSurface();
        void _074_SetQuadrangleSurface();
        void _075_GetQuadrangleSurfaces();
        void _076_SetQuadrangleSurfaces();
        void _077_GetBSplineSurfaceCount();
        void _078_GetBSplineSurface();
        void _079_SetBSplineSurface();
        void _080_GetBSplineSurfaces();
        void _081_SetBSplineSurfaces();
        void _082_GetRotatedSurfaceCount();
        void _083_GetRotatedSurface();
        void _084_SetRotatedSurface();
        void _085_GetRotatedSurfaces();
        void _086_SetRotatedSurfaces();
        void _087_GetPipeSurfaceCount();
        void _088_GetPipeSurface();
        void _089_SetPipeSurface();
        void _090_GetPipeSurfaces();
        void _091_SetPipeSurfaces();
        void _092_GetTrajectorySurfaceCount();
        void _093_GetTrajectorySurface();
        void _094_SetTrajectorySurface();
        void _095_GetTrajectorySurfaces();
        void _096_SetTrajectorySurfaces();
        void _097_GetNurbsSurfaceCount();
        void _098_GetNurbsSurface();
        void _099_SetNurbsSurface();
        void _100_GetNurbsSurfaces();
        void _101_SetNurbsSurfaces();
        void _102_GetComponentSurfaceCount();
        void _103_GetComponentSurface();
        void _104_SetComponentSurface();
        void _105_GetComponentSurfaces();
        void _106_SetComponentSurfaces();
        /// <summary>Miejsce 107: GetSolidCount(out *c_long Count).</summary>
        int GetSolidCount();
        void _108_GetSolid();
        void _109_SetSolid();
        void _110_GetSolids();
        void _111_SetSolids();
        void _112_GetMaterialSolidCount();
        void _113_GetMaterialSolids();
        void _114_GetGasSolidCount();
        void _115_GetGasSolid();
        void _116_SetGasSolid();
        void _117_GetGasSolids();
        void _118_SetGasSolids();
        void _119_GetContactSolidCount();
        void _120_GetContactSolid();
        void _121_SetContactSolid();
        void _122_GetContactSolids();
        void _123_SetContactSolids();
        void _124_GetNullSolidCount();
        void _125_GetNullSolids();
        void _126_GetCompoundSolidCount();
        void _127_GetCompoundSolid();
        void _128_SetCompoundSolid();
        void _129_GetCompoundSolids();
        void _130_SetCompoundSolids();
        void _131_GetOpeningCount();
        void _132_GetOpening();
        void _133_SetOpening();
        void _134_GetOpenings();
        void _135_SetOpenings();
        /// <summary>Miejsce 136: GetNodalSupportCount(out *c_long Count).</summary>
        int GetNodalSupportCount();
        void _137_GetNodalSupport();
        void _138_SetNodalSupport();
        void _139_GetNodalSupports();
        void _140_SetNodalSupports();
        void _141_GetLineSupportCount();
        void _142_GetLineSupport();
        void _143_SetLineSupport();
        void _144_GetLineSupports();
        void _145_SetLineSupports();
        void _146_GetSurfaceSupportCount();
        void _147_GetSurfaceSupport();
        void _148_SetSurfaceSupport();
        void _149_GetSurfaceSupports();
        void _150_SetSurfaceSupports();
        void _151_GetLineHingeCount();
        void _152_GetLineHinge();
        void _153_SetLineHinge();
        void _154_GetLineHinges();
        void _155_SetLineHinges();
        /// <summary>Miejsce 156: GetCrossSectionCount(out *c_long Count).</summary>
        int GetCrossSectionCount();
        /// <summary>Miejsce 157: GetCrossSection(c_long No, c_long At, out *POINTER(ICrossSection) CrossSection).</summary>
        ICrossSection GetCrossSection(int no, int at);
        void _158_SetCrossSection();
        void _159_GetCrossSections();
        void _160_SetCrossSections();
        /// <summary>Miejsce 161: GetMemberHingeCount(out *c_long Count).</summary>
        int GetMemberHingeCount();
        /// <summary>Miejsce 162: GetMemberHinge(c_long No, c_long At, out *POINTER(IMemberHinge) Hinge).</summary>
        IMemberHinge GetMemberHinge(int no, int at);
        void _163_SetMemberHinge();
        void _164_GetMemberHinges();
        void _165_SetMemberHinges();
        void _166_GetMemberEccentricityCount();
        void _167_GetMemberEccentricity();
        void _168_SetMemberEccentricity();
        void _169_GetMemberEccentricities();
        void _170_SetMemberEccentricities();
        void _171_GetMemberDivisionCount();
        void _172_GetMemberDivision();
        void _173_SetMemberDivision();
        void _174_GetMemberDivisions();
        void _175_SetMemberDivisions();
        /// <summary>Miejsce 176: GetMemberCount(out *c_long Count).</summary>
        int GetMemberCount();
        /// <summary>Miejsce 177: GetMember(c_long No, c_long At, out *POINTER(IMember) Member).</summary>
        IMember GetMember(int no, int at);
        void _178_SetMember();
        /// <summary>Miejsce 179: GetMembers(out **SAFEARRAY_Member Members).</summary>
        void GetMembers(out IntPtr members);
        void _180_SetMembers();
        void _181_GetRibCount();
        void _182_GetRib();
        void _183_SetRib();
        void _184_GetRibs();
        void _185_SetRibs();
        void _186_GetMemberElasticFoundationCount();
        void _187_GetMemberElasticFoundation();
        void _188_SetMemberElasticFoundation();
        void _189_GetMemberElasticFoundations();
        void _190_SetMemberElasticFoundations();
        void _191_GetMemberNonlinearityCount();
        void _192_GetMemberNonlinearity();
        void _193_SetMemberNonlinearity();
        void _194_GetMemberNonlinearities();
        void _195_SetMemberNonlinearities();
        /// <summary>Miejsce 196: GetSetOfMembersCount(out *c_long Count).</summary>
        int GetSetOfMembersCount();
        /// <summary>Miejsce 197: GetSetOfMembers(c_long No, c_long At, out *POINTER(ISetOfMembers) Set).</summary>
        ISetOfMembers GetSetOfMembers(int no, int at);
        void _198_SetSetOfMembers();
        void _199_GetSetsOfMembers();
        void _200_SetSetsOfMembers();
        void _201_GetIntersectionOfSurfacesCount();
        void _202_GetIntersectionOfSurfaces();
        void _203_SetIntersectionOfSurfaces();
        void _204_GetIntersectionsOfSurfaces();
        void _205_SetIntersectionsOfSurfaces();
        void _206_GetFeMeshRefinementCount();
        void _207_GetFeMeshRefinement();
        void _208_SetFeMeshRefinement();
        void _209_GetFeMeshRefinements();
        void _210_SetFeMeshRefinements();
        void _211_GetNodalReleaseCount();
        void _212_GetNodalRelease();
        void _213_SetNodalRelease();
        void _214_GetNodalReleases();
        void _215_SetNodalReleases();
        void _216_GetLineReleaseTypeCount();
        void _217_GetLineReleaseType();
        void _218_SetLineReleaseType();
        void _219_GetLineReleaseTypes();
        void _220_SetLineReleaseTypes();
        void _221_GetLineReleaseCount();
        void _222_GetLineRelease();
        void _223_SetLineRelease();
        void _224_GetLineReleases();
        void _225_SetLineReleases();
        void _226_GetConnectionEccentricityCount();
        void _227_GetConnectionEccentricity();
        void _228_SetConnectionEccentricity();
        void _229_GetConnectionEccentricities();
        void _230_SetConnectionEccentricities();
        void _231_GetConnectionOfTwoMembersCount();
        void _232_GetConnectionOfTwoMembers();
        void _233_SetConnectionOfTwoMembers();
        void _234_GetConnectionsOfTwoMembers();
        void _235_SetConnectionsOfTwoMembers();
        void _236_GetTendonCount();
        void _237_GetTendon();
        void _238_SetTendon();
        void _239_GetTendons();
        void _240_SetTendons();
    }

    /// <summary>Pręt (obiekt): <c>GetData</c> oddaje strukturę <see cref="Member"/>.</summary>
    [ComImport, Guid("CB331F28-91B8-4006-8036-3421C90F32A0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMember
    {
        /// <summary>Miejsce 0: GetData(out *Member Member).</summary>
        void GetData(out Member member);
        void _001_SetData();
        void _002_Delete();
        void _003_GetExtraData();
        void _004_GetLocalCoordinateSystem();
        void _005_GetEccentricity();
        void _006_GetEffectiveLengths();
        void _007_SetEffectiveLengths();
        void _008_GetStiffnessFactors();
        void _009_SetStiffnessFactors();
    }

    /// <summary>Przekrój (obiekt): <c>GetData</c> oddaje <see cref="CrossSection"/>.</summary>
    [ComImport, Guid("069B7F41-40F7-4153-B73A-40DA98A8B74A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ICrossSection
    {
        /// <summary>Miejsce 0: GetData(out *CrossSection CrossSection).</summary>
        void GetData(out CrossSection section);
        void _001_SetData();
        void _002_Delete();
        void _003_GetDatabaseCrossSection();
    }

    /// <summary>Materiał (obiekt): <c>GetData</c> oddaje <see cref="Material"/>.</summary>
    [ComImport, Guid("AF55BD83-A0F8-4049-8170-D73956A84D1C"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMaterial
    {
        /// <summary>Miejsce 0: GetData(out *Material Material).</summary>
        void GetData(out Material material);
        void _001_SetData();
        void _002_Delete();
        void _003_GetDatabaseMaterial();
        void _004_GetModel();
    }

    /// <summary>Węzeł (obiekt): <c>GetData</c> oddaje <see cref="Node"/>.</summary>
    [ComImport, Guid("276673A8-D88F-484F-8BDA-CD857589730D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface INode
    {
        /// <summary>Miejsce 0: GetData(out *Node Node).</summary>
        void GetData(out Node node);
        void _001_SetData();
        void _002_Delete();
        void _003_SetCoorinateSystem();
    }

    /// <summary>Linia (obiekt): <c>GetData</c> oddaje <see cref="Line"/>.</summary>
    [ComImport, Guid("F15840C7-99A5-4D84-951D-6A1725C7E08A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ILine
    {
        /// <summary>Miejsce 0: GetData(out *Line Line).</summary>
        void GetData(out Line line);
        void _001_SetData();
        void _002_Delete();
        void _003_GetLocalCoordinateSystem();
        void _004_GetPointOnLine();
    }

    /// <summary>Obliczenia: wyniki dla przypadku albo kombinacji (<c>GetResultsInFeNodes</c>).</summary>
    [ComImport, Guid("FF91D5AC-B232-415C-B276-FB5957FAF40E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ICalculation
    {
        void _000_GetSettings();
        void _001_SetSettings();
        void _002_GetOptions();
        void _003_SetOptions();
        void _004_GetPrecisionAndTolerance();
        void _005_SetPrecisionAndTolerance();
        void _006_GetNonlinearities();
        void _007_SetNonlinearities();
        void _008_GetReactivation();
        void _009_SetReactivation();
        void _010_GetInternalPressure();
        void _011_SetInternalPressure();
        void _012_GetSolverMethod();
        void _013_SetSolverMethod();
        void _014_GetBendingTheory();
        void _015_SetBendingTheory();
        void _016_GetSolverVersion();
        void _017_SetSolverVersion();
        void _018_GenerateFeMesh();
        void _019_DeleteFeMesh();
        void _020_GetFeMesh();
        void _021_GetFeMeshSettings();
        void _022_Calculate();
        void _023_CalculateAll();
        void _024_CalculateApp();
        void _025_CalculateModules();
        void _026_CalculateBatch();
        void _027_CalculateAsync();
        void _028_CalculateAsyncAll();
        void _029_CalculateAsyncApp();
        void _030_CalculateAsyncModules();
        void _031_CalculateAsyncBatch();
        /// <summary>Miejsce 32: GetResultsInRasterPoints(c_long Type, c_long No, out *POINTER(IResults) Results).</summary>
        IResults GetResultsInRasterPoints(int type, int no);
        /// <summary>Miejsce 33: GetResultsInFeNodes(c_long Type, c_long No, out *POINTER(IResults) Results).</summary>
        IResults GetResultsInFeNodes(int type, int no);
        void _034_Clean();
        void _035_ExportIsolines();
        void _036_GetSubCombinations();
    }

    /// <summary>Wyniki jednego obciążenia: siły w prętach, przemieszczenia, reakcje (tablice SAFEARRAY rekordów).</summary>
    [ComImport, Guid("BAA1E072-410D-4605-B3CE-A6580E082ADE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IResults
    {
        void _000_GetCalculationTime();
        void _001_AddRibsToMembers();
        void _002_SubtractRibsFromSurfaces();
        void _003_GetSummary();
        void _004_GetMaximum();
        /// <summary>Miejsce 5: GetNodalSupportForcesCount(c_long NodeNo, c_long At, VARIANT_BOOL LocalCS, out *c_long Count).</summary>
        int GetNodalSupportForcesCount(int nodeNo, int at, [MarshalAs(UnmanagedType.VariantBool)] bool localCS);
        /// <summary>Miejsce 6: GetNodalSupportForces(c_long NodeNo, c_long At, VARIANT_BOOL LocalCS, out **SAFEARRAY_NodalSupportForces Results).</summary>
        void GetNodalSupportForces(int nodeNo, int at, [MarshalAs(UnmanagedType.VariantBool)] bool localCS, out IntPtr results);
        /// <summary>Miejsce 7: GetNodalExtremeSupportForces(c_long NodeNo, c_long At, VARIANT_BOOL LocalCS, out **SAFEARRAY_NodalSupportForces Results).</summary>
        void GetNodalExtremeSupportForces(int nodeNo, int at, [MarshalAs(UnmanagedType.VariantBool)] bool localCS, out IntPtr results);
        void _008_GetAllNodalSupportForcesCount();
        void _009_GetAllNodalSupportForces();
        void _010_GetAllNodalExtremeSupportForces();
        void _011_GetNodalSummarySupportForces();
        void _012_GetNodalDeformationsCount();
        void _013_GetNodalDeformations();
        void _014_GetAllNodalDeformationsCount();
        void _015_GetAllNodalDeformations();
        void _016_GetNodalExtremeDeformations();
        void _017_GetLineSupportForcesCount();
        void _018_GetLineSupportForces();
        void _019_GetLineExtremeSupportForces();
        void _020_GetLinesSupportForcesCount();
        void _021_GetLinesSupportForces();
        void _022_GetLinesExtremeSupportForces();
        void _023_GetLinesSummarySupportForces();
        /// <summary>Miejsce 24: GetMemberDeformationsCount(c_long MemberNo, c_long At, c_long Axes, out *c_long Count).</summary>
        int GetMemberDeformationsCount(int memberNo, int at, int axes);
        /// <summary>Miejsce 25: GetMemberDeformations(c_long MemberNo, c_long At, c_long Axes, out **SAFEARRAY_MemberDeformations Results).</summary>
        void GetMemberDeformations(int memberNo, int at, int axes, out IntPtr results);
        /// <summary>Miejsce 26: GetMemberExtremeDeformations(c_long MemberNo, c_long At, c_long Axes, out **SAFEARRAY_MemberDeformations Results).</summary>
        void GetMemberExtremeDeformations(int memberNo, int at, int axes, out IntPtr results);
        void _027_GetMembersDeformationsCount();
        void _028_GetMembersDeformations();
        void _029_GetMembersExtremeDeformations();
        /// <summary>Miejsce 30: GetMemberInternalForcesCount(c_long MemberNo, c_long At, VARIANT_BOOL MemberAxes, out *c_long Count).</summary>
        int GetMemberInternalForcesCount(int memberNo, int at, [MarshalAs(UnmanagedType.VariantBool)] bool memberAxes);
        /// <summary>Miejsce 31: GetMemberInternalForces(c_long MemberNo, c_long At, VARIANT_BOOL MemberAxes, out **SAFEARRAY_MemberForces Results).</summary>
        void GetMemberInternalForces(int memberNo, int at, [MarshalAs(UnmanagedType.VariantBool)] bool memberAxes, out IntPtr results);
        /// <summary>Miejsce 32: GetMemberExtremeInternalForces(c_long MemberNo, c_long At, VARIANT_BOOL MemberAxes, out **SAFEARRAY_MemberForces Results).</summary>
        void GetMemberExtremeInternalForces(int memberNo, int at, [MarshalAs(UnmanagedType.VariantBool)] bool memberAxes, out IntPtr results);
        void _033_GetMembersInternalForcesCount();
        void _034_GetMembersInternalForces();
        void _035_GetMembersExtremeInternalForces();
        void _036_GetMemberContactForcesCount();
        void _037_GetMemberContactForces();
        void _038_GetMemberExtremeContactForces();
        void _039_GetMembersContactForcesCount();
        void _040_GetMembersContactForces();
        void _041_GetMembersExtremeContactForces();
        void _042_GetSetOfMembersInternalForcesCount();
        void _043_GetSetOfMembersInternalForces();
        void _044_GetSetOfMembersExtremeInternalForces();
        void _045_GetSetsOfMembersInternalForcesCount();
        void _046_GetSetsOfMembersInternalForces();
        void _047_GetSetsOfMembersExtremeInternalForces();
        void _048_GetCrossSectionInternalForcesCount();
        void _049_GetCrossSectionInternalForces();
        void _050_GetCrossSectionExtremeInternalForces();
        void _051_GetCrossSectionsInternalForcesCount();
        void _052_GetCrossSectionsInternalForces();
        void _053_GetCrossSectionsExtremeInternalForces();
        void _054_GetSurfaceDeformationsCount();
        void _055_GetSurfaceDeformations();
        void _056_GetSurfacesDeformationsCount();
        void _057_GetSurfacesDeformations();
        void _058_GetSurfaceInternalForcesCount();
        void _059_GetSurfaceInternalForces();
        void _060_GetSurfacesInternalForcesCount();
        void _061_GetSurfacesInternalForces();
        void _062_GetSurfaceBasicStressesCount();
        void _063_GetSurfaceBasicStresses();
        void _064_GetSurfacesBasicStressesCount();
        void _065_GetSurfacesBasicStresses();
        void _066_GetSurfacePrincipalStressesCount();
        void _067_GetSurfacePrincipalStresses();
        void _068_GetSurfacesPrincipalStressesCount();
        void _069_GetSurfacesPrincipalStresses();
        void _070_GetSurfaceOtherStressesCount();
        void _071_GetSurfaceOtherStresses();
        void _072_GetSurfacesOtherStressesCount();
        void _073_GetSurfacesOtherStresses();
        void _074_GetSurfaceContactStressesCount();
        void _075_GetSurfaceContactStresses();
        void _076_GetSurfacesContactStressesCount();
        void _077_GetSurfacesContactStresses();
        void _078_GetSurfaceEquivalentStressesCount();
        void _079_GetSurfaceEquivalentStresses();
        void _080_GetSurfacesEquivalentStressesCount();
        void _081_GetSurfacesEquivalentStresses();
        void _082_GetSurfaceBasicStrainsCount();
        void _083_GetSurfaceBasicStrains();
        void _084_GetSurfacesBasicStrainsCount();
        void _085_GetSurfacesBasicStrains();
        void _086_GetSurfacePrincipalStrainsCount();
        void _087_GetSurfacePrincipalStrains();
        void _088_GetSurfacesPrincipalStrainsCount();
        void _089_GetSurfacesPrincipalStrains();
        void _090_GetSurfaceMaximumStrainsCount();
        void _091_GetSurfaceMaximumStrains();
        void _092_GetSurfacesMaximumStrainsCount();
        void _093_GetSurfacesMaximumStrains();
        void _094_GetSurfaceStrainsCount();
        void _095_GetSurfaceStrains();
        void _096_GetSurfacesStrainsCount();
        void _097_GetSurfacesStrains();
        void _098_GetSurfacePlasticStrainsCount();
        void _099_GetSurfacePlasticStrains();
        void _100_GetSurfacesPlasticStrainsCount();
        void _101_GetSurfacesPlasticStrains();
        void _102_GetSurfaceCrackStrainsCount();
        void _103_GetSurfaceCrackStrains();
        void _104_GetSurfacesCrackStrainsCount();
        void _105_GetSurfacesCrackStrains();
        void _106_GetSurfaceCriteriaCount();
        void _107_GetSurfaceCriteria();
        void _108_GetSurfacesCriteriaCount();
        void _109_GetSurfacesCriteria();
        void _110_GetSolidDeformationsCount();
        void _111_GetSolidDeformations();
        void _112_GetSolidsDeformationsCount();
        void _113_GetSolidsDeformations();
        void _114_GetSolidStressesCount();
        void _115_GetSolidStresses();
        void _116_GetSolidsStressesCount();
        void _117_GetSolidsStresses();
        void _118_GetSolidStrainsCount();
        void _119_GetSolidStrains();
        void _120_GetSolidsStrainsCount();
        void _121_GetSolidsStrains();
        void _122_GetSolidPlasticStrainsCount();
        void _123_GetSolidPlasticStrains();
        void _124_GetSolidsPlasticStrainsCount();
        void _125_GetSolidsPlasticStrains();
        void _126_GetSolidCriteriaCount();
        void _127_GetSolidCriteria();
        void _128_GetSolidsCriteriaCount();
        void _129_GetSolidsCriteria();
        void _130_GetSolidGasPressureCount();
        void _131_GetSolidGasPressure();
        void _132_GetSolidsGasPressureCount();
        void _133_GetSolidsGasPressure();
        void _134_GetSolidStressesAtCenterOfGravityCount();
        void _135_GetSolidStressesAtCenterOfGravity();
        void _136_GetSolidsStressesAtCenterOfGravityCount();
        void _137_GetSolidsStressesAtCenterOfGravity();
        void _138_GetSolidStrainsAtCenterOfGravityCount();
        void _139_GetSolidStrainsAtCenterOfGravity();
        void _140_GetSolidsStrainsAtCenterOfGravityCount();
        void _141_GetSolidsStrainsAtCenterOfGravity();
        void _142_GetSolidGasPressureCenterCount();
        void _143_GetSolidGasPressureCenter();
        void _144_GetSolidsGasPressureCenterCount();
        void _145_GetSolidsGasPressureCenter();
    }

    /// <summary>Obciążenia: przypadki (PO), kombinacje obciążeń (KO) i kombinacje wyników (KW).</summary>
    [ComImport, Guid("CBE1235F-5297-4D50-B38B-5BCAD4621E35"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ILoads
    {
        void _000_PrepareModification();
        void _001_FinishModification();
        void _002_CancelModification();
        /// <summary>Miejsce 3: HasAnyResults(out *VARIANT_BOOL HasResults).</summary>
        [return: MarshalAs(UnmanagedType.VariantBool)] bool HasAnyResults();
        void _004_HasAnyLoadingResults();
        void _005_HasLoadingResults();
        void _006_Clean();
        void _007_DeleteLoading();
        void _008_GetLastObjectNo();
        /// <summary>Miejsce 9: GetLoadCaseCount(out *c_long Count).</summary>
        int GetLoadCaseCount();
        /// <summary>Miejsce 10: GetLoadCase(c_long No, c_long At, out *POINTER(ILoadCase) Load).</summary>
        ILoadCase GetLoadCase(int no, int at);
        void _011_SetLoadCase();
        void _012_GetLoadCases();
        void _013_SetLoadCases();
        /// <summary>Miejsce 14: GetLoadCombinationCount(out *c_long Count).</summary>
        int GetLoadCombinationCount();
        /// <summary>Miejsce 15: GetLoadCombination(c_long No, c_long At, out *POINTER(ILoadCombination) Load).</summary>
        ILoadCombination GetLoadCombination(int no, int at);
        void _016_SetLoadCombination();
        void _017_GetLoadCombinations();
        void _018_SetLoadCombinations();
        /// <summary>Miejsce 19: GetResultCombinationCount(out *c_long Count).</summary>
        int GetResultCombinationCount();
        /// <summary>Miejsce 20: GetResultCombination(c_long No, c_long At, out *POINTER(IResultCombination) Load).</summary>
        IResultCombination GetResultCombination(int no, int at);
        void _021_SetResultCombination();
        void _022_GetResultCombinations();
        void _023_SetResultCombinations();
        void _024_GetSuperCombinationCount();
        void _025_GetSuperCombination();
        void _026_SetSuperCombination();
        void _027_GetSuperCombinations();
        void _028_SetSuperCombinations();
        void _029_GenerateAreaLoad();
    }

    /// <summary>Przypadek obciążeń (obiekt).</summary>
    [ComImport, Guid("8BE06E1C-DA5E-4C02-983D-890EBB04EF78"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ILoadCase
    {
        void _000_PrepareModification();
        void _001_FinishModification();
        void _002_CancelModification();
        void _003_IgnoreErrors();
        void _004_EnableSelections();
        void _005_SelectObjects();
        void _006_GetLastObjectNo();
        void _007_Clean();
        void _008_DeleteObjects();
        void _009_SetObjectID();
        void _010_PlausibilityCheck();
        /// <summary>Miejsce 11: HasResults(out *VARIANT_BOOL HasResults).</summary>
        [return: MarshalAs(UnmanagedType.VariantBool)] bool HasResults();
        /// <summary>Miejsce 12: GetData(out *LoadCase LCase).</summary>
        void GetData(out LoadCase loadCase);
        void _013_SetData();
        void _014_Delete();
        void _015_GetAnalysisParameters();
        void _016_SetAnalysisParameters();
        void _017_GetIncrementallyIncreasingLoading();
        void _018_SetIncrementallyIncreasingLoading();
        void _019_GetNodalLoadCount();
        void _020_GetNodalLoad();
        void _021_SetNodalLoad();
        void _022_GetNodalLoads();
        void _023_SetNodalLoads();
        void _024_GetMemberLoadCount();
        void _025_GetMemberLoad();
        void _026_SetMemberLoad();
        void _027_GetMemberLoads();
        void _028_SetMemberLoads();
        void _029_GetLineLoadCount();
        void _030_GetLineLoad();
        void _031_SetLineLoad();
        void _032_GetLineLoads();
        void _033_SetLineLoads();
        void _034_GetSurfaceLoadCount();
        void _035_GetSurfaceLoad();
        void _036_SetSurfaceLoad();
        void _037_GetSurfaceLoads();
        void _038_SetSurfaceLoads();
        void _039_GetSolidLoadCount();
        void _040_GetSolidLoad();
        void _041_SetSolidLoad();
        void _042_GetSolidLoads();
        void _043_SetSolidLoads();
        void _044_GetFreeConcentratedLoadCount();
        void _045_GetFreeConcentratedLoad();
        void _046_SetFreeConcentratedLoad();
        void _047_GetFreeConcentratedLoads();
        void _048_SetFreeConcentratedLoads();
        void _049_GetFreeLineLoadCount();
        void _050_GetFreeLineLoad();
        void _051_SetFreeLineLoad();
        void _052_GetFreeLineLoads();
        void _053_SetFreeLineLoads();
        void _054_GetFreeRectangularLoadCount();
        void _055_GetFreeRectangularLoad();
        void _056_SetFreeRectangularLoad();
        void _057_GetFreeRectangularLoads();
        void _058_SetFreeRectangularLoads();
        void _059_GetFreeCircularLoadCount();
        void _060_GetFreeCircularLoad();
        void _061_SetFreeCircularLoad();
        void _062_GetFreeCircularLoads();
        void _063_SetFreeCircularLoads();
        void _064_GetFreePolygonLoadCount();
        void _065_GetFreePolygonLoad();
        void _066_SetFreePolygonLoad();
        void _067_GetFreePolygonLoads();
        void _068_SetFreePolygonLoads();
        void _069_GetImposedNodalDeformationCount();
        void _070_GetImposedNodalDeformation();
        void _071_SetImposedNodalDeformation();
        void _072_GetImposedNodalDeformations();
        void _073_SetImposedNodalDeformations();
        void _074_GetImposedLineDisplacementCount();
        void _075_GetImposedLineDisplacement();
        void _076_SetImposedLineDisplacement();
        void _077_GetImposedLineDisplacements();
        void _078_SetImposedLineDisplacements();
        void _079_GetImperfectionCount();
        void _080_GetImperfection();
        void _081_SetImperfection();
        void _082_GetImperfections();
        void _083_SetImperfections();
    }

    /// <summary>Kombinacja obciążeń (obiekt).</summary>
    [ComImport, Guid("5D1B5D52-03EC-47C1-883D-95D7C6C9D86C"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ILoadCombination
    {
        /// <summary>Miejsce 0: GetData(out *LoadCombination Combination).</summary>
        void GetData(out LoadCombination combination);
        void _001_SetData();
        void _002_Delete();
        /// <summary>Miejsce 3: HasResults(out *VARIANT_BOOL HasResults).</summary>
        [return: MarshalAs(UnmanagedType.VariantBool)] bool HasResults();
        void _004_GetAnalysisParameters();
        void _005_SetAnalysisParameters();
        void _006_GetIncrementallyIncreasingLoading();
        void _007_SetIncrementallyIncreasingLoading();
        void _008_GetLoadings();
        void _009_SetLoadings();
    }

    /// <summary>Kombinacja wyników (obiekt).</summary>
    [ComImport, Guid("788520D2-12BE-49CB-B1ED-837C9E259C28"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IResultCombination
    {
        /// <summary>Miejsce 0: GetData(out *ResultCombination Combination).</summary>
        void GetData(out ResultCombination combination);
        void _001_SetData();
        void _002_Delete();
        /// <summary>Miejsce 3: HasResults(out *VARIANT_BOOL HasResults).</summary>
        [return: MarshalAs(UnmanagedType.VariantBool)] bool HasResults();
        void _004_GetQuadraticCombination();
        void _005_SetQuadraticCombination();
        void _006_GetLoadings();
        void _007_SetLoadings();
    }

    /// <summary>Aktywny widok: <c>PickObjects</c> otwiera w RFEM okno wyboru i czeka na kliknięcie użytkownika.</summary>
    [ComImport, Guid("E49ED436-CE67-4A2B-9CC1-BCBA70B1B100"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IView
    {
        /// <summary>Miejsce 0: PickObjects(c_long Tool, out *BSTR Objects).</summary>
        [return: MarshalAs(UnmanagedType.BStr)] string PickObjects(int tool);
        void _001_CopyGraphicsToClipboard();
        void _002_CleanClipboard();
        void _003_GetViewPoint();
        void _004_SetViewPoint();
        void _005_SelectLoading();
        void _006_ShowLoads();
        void _007_ShowPerspective();
        void _008_ShowAll();
    }

    /// <summary>Zbiór prętów (obiekt).</summary>
    [ComImport, Guid("9ED9030A-B3EB-43D2-9DAF-4575A2B19B88"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ISetOfMembers
    {
        /// <summary>Miejsce 0: GetData(out *SetOfMembers Set).</summary>
        void GetData(out SetOfMembers set);
        void _001_SetData();
        void _002_Delete();
    }

    /// <summary>Przegub prętowy (obiekt).</summary>
    [ComImport, Guid("7B559485-AFEE-4B2C-8D2E-5ADBBCC1F3A5"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMemberHinge
    {
        /// <summary>Miejsce 0: GetData(out *MemberHinge Hinge).</summary>
        void GetData(out MemberHinge hinge);
        void _001_SetData();
        void _002_Delete();
        void _003_GetNonlinearity();
    }

    // Struktury rekordów RF-COM (układ i rozmiary zmierzone na bibliotece v5.7, x64; sprawdzane w testach przez Marshal.SizeOf/OffsetOf).
    // Jednostki SI: m, m², m⁴, Pa, N, Nm.

    /// <summary>Obrót pręta albo linii (24 B).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct Rotation
    {
        public int Type;        // RotationType: 1 NoneRotation, 2 Angle, 3 HelpNode
        public double Angle;    // rad
        public int HelpNodeNo;
        public int Plane;
    }

    /// <summary>Punkt albo trójka składowych (24 B).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct Point3D
    {
        public double X;
        public double Y;
        public double Z;
    }

    /// <summary>Pręt (128 B).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct Member
    {
        public int No;
        [MarshalAs(UnmanagedType.BStr)] public string ID;
        [MarshalAs(UnmanagedType.BStr)] public string Comment;
        [MarshalAs(UnmanagedType.BStr)] public string Tag;
        [MarshalAs(UnmanagedType.VariantBool)] public bool IsValid;
        [MarshalAs(UnmanagedType.VariantBool)] public bool IsGenerated;
        public int LineNo;
        public int Type;        // MemberType
        public Rotation Rotation;
        public int StartCrossSectionNo;
        public int EndCrossSectionNo;
        public int TaperShape;
        public int StartHingeNo;
        public int EndHingeNo;
        public int EccentricityNo;
        public int DivisionNo;
        public int FoundationNo;
        public int NonlinearityNo;
        public int RibNo;
        public double Length;   // m
        public double Weight;   // kg
    }

    /// <summary>Przekrój (144 B): A i pola momentów bezwładności w m², m⁴.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct CrossSection
    {
        public int No;
        [MarshalAs(UnmanagedType.BStr)] public string ID;
        [MarshalAs(UnmanagedType.BStr)] public string Comment;
        [MarshalAs(UnmanagedType.BStr)] public string Tag;
        [MarshalAs(UnmanagedType.VariantBool)] public bool IsValid;
        [MarshalAs(UnmanagedType.BStr)] public string Description;
        [MarshalAs(UnmanagedType.BStr)] public string TextID;
        [MarshalAs(UnmanagedType.VariantBool)] public bool UserDefined;
        public int MaterialNo;
        public double Rotation;
        public double Angle;
        public double TorsionMoment;   // It [m⁴]
        public double BendingMomentY;  // Iy [m⁴]
        public double BendingMomentZ;  // Iz [m⁴]
        public double AxialArea;       // A [m²]
        public double ShearAreaY;
        public double ShearAreaZ;
        public double TemperatureLoadWidth;
        public double TemperatureLoadDepth;
    }

    /// <summary>Materiał (120 B): E, G w Pa, ciężar objętościowy w N/m³.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct Material
    {
        public int No;
        [MarshalAs(UnmanagedType.BStr)] public string ID;
        [MarshalAs(UnmanagedType.BStr)] public string Comment;
        [MarshalAs(UnmanagedType.BStr)] public string Tag;
        [MarshalAs(UnmanagedType.VariantBool)] public bool IsValid;
        [MarshalAs(UnmanagedType.BStr)] public string Description;
        [MarshalAs(UnmanagedType.BStr)] public string TextID;
        [MarshalAs(UnmanagedType.VariantBool)] public bool UserDefined;
        public double ElasticityModulus;
        public double ShearModulus;
        public double PoissonRatio;
        public double SpecificWeight;
        public double ThermalExpansion;
        public double PartialSafetyFactor;
        public int ModelType;
    }

    /// <summary>Węzeł (88 B): współrzędne w m.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct Node
    {
        public int No;
        [MarshalAs(UnmanagedType.BStr)] public string ID;
        [MarshalAs(UnmanagedType.BStr)] public string Comment;
        [MarshalAs(UnmanagedType.BStr)] public string Tag;
        [MarshalAs(UnmanagedType.VariantBool)] public bool IsValid;
        [MarshalAs(UnmanagedType.VariantBool)] public bool IsGenerated;
        public int RefObjectNo;
        public double X;
        public double Y;
        public double Z;
        public int CS;
        public int Type;
        public double Delta1;
        public double Delta2;
    }

    /// <summary>Linia (96 B): <c>NodeList</c> to „64,38”; <c>ControlPoints</c> to wskaźnik SAFEARRAY (zwalniany przez wołającego).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct Line
    {
        public int No;
        [MarshalAs(UnmanagedType.BStr)] public string ID;
        [MarshalAs(UnmanagedType.BStr)] public string Comment;
        [MarshalAs(UnmanagedType.BStr)] public string Tag;
        [MarshalAs(UnmanagedType.VariantBool)] public bool IsValid;
        [MarshalAs(UnmanagedType.VariantBool)] public bool IsGenerated;
        public int Type;        // LineType: 1 Polyline, 4 Arc, …
        public int NodeCount;
        [MarshalAs(UnmanagedType.BStr)] public string NodeList;
        public double Length;   // m
        public Rotation Rotation;
        public IntPtr ControlPoints;
    }

    /// <summary>Zbiór prętów (80 B).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct SetOfMembers
    {
        public int No;
        [MarshalAs(UnmanagedType.BStr)] public string ID;
        [MarshalAs(UnmanagedType.BStr)] public string Comment;
        [MarshalAs(UnmanagedType.BStr)] public string Tag;
        [MarshalAs(UnmanagedType.VariantBool)] public bool IsValid;
        [MarshalAs(UnmanagedType.BStr)] public string Description;
        public int Type;        // 1 ContinuousMembers, 2 GroupOfMembers
        [MarshalAs(UnmanagedType.BStr)] public string MemberList;
        public double Length;
        public double Weight;
    }

    /// <summary>Układ osi zdefiniowany przez użytkownika (56 B) — część przegubu i podpory; nie czytamy pól.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct UserDefinedAxisSystem
    {
        public int Type;
        public int CoordinateSystemNo;
        public Point3D Point1;
        public Point3D Point2;
    }

    /// <summary>Przegub prętowy (184 B): stałe sprężyn (N/m, Nm/rad), 0 = zwolnienie, -1 = sztywno (w RF-COM: brak zwolnienia = stała „nieskończona”).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct MemberHinge
    {
        public int No;
        [MarshalAs(UnmanagedType.BStr)] public string ID;
        [MarshalAs(UnmanagedType.BStr)] public string Comment;
        [MarshalAs(UnmanagedType.BStr)] public string Tag;
        [MarshalAs(UnmanagedType.VariantBool)] public bool IsValid;
        public int ReferenceSystem;
        public UserDefinedAxisSystem UserDefinedReferenceSystem;
        public double TranslationalConstantX;
        public double TranslationalConstantY;
        public double TranslationalConstantZ;
        public double RotationalConstantX;
        public double RotationalConstantY;
        public double RotationalConstantZ;
        public int TranslationalNonlinearityX;
        public int TranslationalNonlinearityY;
        public int TranslationalNonlinearityZ;
        public int RotationalNonlinearityX;
        public int RotationalNonlinearityY;
        public int RotationalNonlinearityZ;
        [MarshalAs(UnmanagedType.VariantBool)] public bool TranslationalSpecialX;
        [MarshalAs(UnmanagedType.VariantBool)] public bool TranslationalSpecialY;
        [MarshalAs(UnmanagedType.VariantBool)] public bool TranslationalSpecialZ;
        [MarshalAs(UnmanagedType.VariantBool)] public bool RotationalSpecialX;
        [MarshalAs(UnmanagedType.VariantBool)] public bool RotationalSpecialY;
        [MarshalAs(UnmanagedType.VariantBool)] public bool RotationalSpecialZ;
    }

    /// <summary>Obciążenie: typ (1 PO, 2 KO, 3 KW, 4 superkombinacja) i numer (8 B).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct Loading
    {
        public int Type;
        public int No;
    }

    /// <summary>Przypadek obciążeń (96 B).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct LoadCase
    {
        public Loading Loading;
        [MarshalAs(UnmanagedType.BStr)] public string ID;
        [MarshalAs(UnmanagedType.BStr)] public string Comment;
        [MarshalAs(UnmanagedType.BStr)] public string Tag;
        [MarshalAs(UnmanagedType.VariantBool)] public bool IsValid;
        [MarshalAs(UnmanagedType.BStr)] public string Description;
        [MarshalAs(UnmanagedType.VariantBool)] public bool ToSolve;
        public int ActionCategory;
        [MarshalAs(UnmanagedType.BStr)] public string UserDefinedAction;
        [MarshalAs(UnmanagedType.VariantBool)] public bool SelfWeight;
        public Point3D SelfWeightFactor;
    }

    /// <summary>Kombinacja obciążeń (64 B): <c>Definition</c> np. „1.35*LC1 + 1.5*LC11”.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct LoadCombination
    {
        public Loading Loading;
        [MarshalAs(UnmanagedType.BStr)] public string ID;
        [MarshalAs(UnmanagedType.BStr)] public string Comment;
        [MarshalAs(UnmanagedType.BStr)] public string Tag;
        [MarshalAs(UnmanagedType.VariantBool)] public bool IsValid;
        public int DesignSituation;
        [MarshalAs(UnmanagedType.BStr)] public string Description;
        [MarshalAs(UnmanagedType.VariantBool)] public bool ToSolve;
        [MarshalAs(UnmanagedType.BStr)] public string Definition;
    }

    /// <summary>Kombinacja wyników (64 B): <c>Definition</c> np. „CO1/p or CO2/p”.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct ResultCombination
    {
        public Loading Loading;
        [MarshalAs(UnmanagedType.BStr)] public string ID;
        [MarshalAs(UnmanagedType.BStr)] public string Comment;
        [MarshalAs(UnmanagedType.BStr)] public string Tag;
        [MarshalAs(UnmanagedType.VariantBool)] public bool IsValid;
        public int DesignSituation;
        [MarshalAs(UnmanagedType.BStr)] public string Description;
        [MarshalAs(UnmanagedType.VariantBool)] public bool ToSolve;
        [MarshalAs(UnmanagedType.BStr)] public string Definition;
    }

    /// <summary>Siły w punkcie pręta (88 B): <c>Forces</c> = N, Vy, Vz [N]; <c>Moments</c> = Mt, My, Mz [Nm]; <c>Location</c> x [m].</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct MemberForces
    {
        public int MemberNo;
        public int CrossSectionNo;
        public int SetNo;
        public int Flag;        // ResultsFlag: 1 wartość, 2 lewa strona, 3 prawa strona
        public int Type;        // przy ekstremach: które ekstremum (kolejność max/min N, Vy, Vz, Mt, My, Mz)
        public double Location;
        public Point3D Forces;
        public Point3D Moments;
        [MarshalAs(UnmanagedType.BStr)] public string CorrespondingLoading;
    }

    /// <summary>Przemieszczenia w punkcie pręta (80 B): [m] i [rad].</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct MemberDeformations
    {
        public int MemberNo;
        public int Flag;
        public int Type;
        public double Location;
        public double TotalDisplacement;
        public Point3D Displacements;
        public Point3D Rotations;
    }

    /// <summary>Reakcje w węźle podporowym (64 B): [N], [Nm].</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct NodalSupportForces
    {
        public int NodeNo;
        public int Type;
        public Point3D Forces;
        public Point3D Moments;
        [MarshalAs(UnmanagedType.BStr)] public string CorrespondingLoading;
    }
}

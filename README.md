# HTTP Request Audit Logging POC

Ez a POC projekt bemutatja a HTTP request audit logging különböző megvalósítási lehetőségeit .NET Framework 4.8 Web API alkalmazásokban.

## Projekt Struktúra

```
AuditLoginPOC/
├── AuditLoginPOC.Core/           # Közös interfészek és implementációk
├── AuditLoginPOC.WebApi/         # Web API alkalmazás
├── AuditLoginPOC.Tests/          # Reqnroll tesztek
├── AuditLoginPOC.Benchmarks/     # Teljesítmény benchmarkok
└── README.md
```

## Főbb Funkciók

### 1. Request Body Újraolvasása
- **ContentReplacementCapturingService**: HttpContent újraolvasása és újraépítése
- **StreamWrappingCapturingService**: Stream wrapper használatával
- **PostProcessingCapturingService**: Model binding utáni feldolgozás

### 2. Eredeti Request Body Loggolása
- Malformed JSON kezelése
- Raw request data megőrzése
- Stream state preservation

### 3. DoS Protection
- **SizeLimitProtectionService**: Méret alapú védelem
- **StreamingDigestProtectionService**: Hash alapú védelem
- **CircuitBreakerProtectionService**: Rate limiting

### 4. FluentValidation Integráció
- **Person Model**: FirstName, LastName, Email, Age validáció
- **Extra Field Handling**: A contract-ban nem szereplő mezők (pl. Gender) csendben ignorálódnak
- **Audit Logging**: Az extra field-ek megmaradnak az audit logban
- **Dependency Injection**: Validator-ok DI konténeren keresztül injektálódnak

### 5. Tesztek
- Reqnroll acceptance tesztek (XUnit alapú)
- Unit tesztek kritikus funkciókra (XUnit + Shouldly assertion library)
- Teljesítmény benchmarkok

## Telepítés és Futtatás

### Előfeltételek
- .NET Framework 4.8
- Visual Studio 2019/2022 vagy .NET CLI

### Build
```bash
dotnet build AuditLoginPOC.sln
```

### Tesztek Futtatása
```bash
# Összes teszt
dotnet test AuditLoginPOC.Tests/

# Csak Unit tesztek
dotnet test AuditLoginPOC.Tests/ --filter "Category=UnitTest"

# Csak Acceptance tesztek (Reqnroll)
dotnet test AuditLoginPOC.Tests/ --filter "Category=AcceptanceTest"
```

### Benchmarkok Futtatása
```bash
dotnet run --project AuditLoginPOC.Benchmarks/
```

### Web API Futtatása
```bash
dotnet run --project AuditLoginPOC.WebApi/
```

## API Endpointok

### Test Endpoints
- `POST /api/test/echo` - Egyszerű echo endpoint
- `POST /api/test/malformed` - Malformed JSON teszt
- `POST /api/test/large` - Nagy request teszt
- `POST /api/test/validation` - Person validáció teszt (FluentValidation)
- `POST /api/test/error` - Exception teszt

## Tesztelési Példák

### 1. Normál JSON Request
```bash
curl -X POST http://localhost:5000/api/test/echo \
  -H "Content-Type: application/json" \
  -d '{"name":"test","value":123}'
```

### 2. Malformed JSON Request
```bash
curl -X POST http://localhost:5000/api/test/malformed \
  -H "Content-Type: application/json" \
  -d '{ invalid json }'
```

### 3. Nagy Request
```bash
curl -X POST http://localhost:5000/api/test/large \
  -H "Content-Type: application/json" \
  -d '{"data":"'$(printf 'x%.0s' {1..1000000})'"}'
```

### 4. Person Validáció (Extra Field Teszt)
```bash
# Sikeres validáció extra field-del
curl -X POST http://localhost:5000/api/test/validation \
  -H "Content-Type: application/json" \
  -d '{
    "FirstName": "John",
    "LastName": "Doe", 
    "Email": "john.doe@example.com",
    "Age": 32,
    "Gender": "male"
  }'

# Validáció hiba extra field-del
curl -X POST http://localhost:5000/api/test/validation \
  -H "Content-Type: application/json" \
  -d '{
    "FirstName": "",
    "LastName": "",
    "Email": "invalid-email",
    "Age": -1,
    "Gender": "male"
  }'
```

**Megjegyzés**: A `Gender` extra field-et a deszerializáció és validáció csendben ignorálja, de az audit logba bekerül.

## Reqnroll Tesztek

A projekt Reqnroll acceptance teszteket tartalmaz a következő területekre:

### Kritikus Követelmények Tesztelése
- ✅ Request body újraolvasása (deszerializáláskor, validáláskor)
- ✅ Eredeti request body loggolása
- ✅ Malformed JSON kezelése
- ✅ Nagy request kezelése
- ✅ Validation error capture
- ✅ Exception handling

### Teszt Scenariók
1. **Capture normal JSON request body**
2. **Capture malformed JSON request body**
3. **Handle large request with size limit protection**
4. **Capture request with validation errors**
5. **Handle request that throws an exception**
6. **Capture request headers and metadata**
7. **Handle request with JWT authentication**

## Benchmark Eredmények

A benchmarkok összehasonlítják a különböző audit logging megközelítések teljesítményét:

### Mért Metrikák
- **Mean**: Átlagos végrehajtási idő
- **Allocated**: Műveletenként lefoglalt memória
- **Gen0/Gen1/Gen2**: Garbage collection generációk

### Összehasonlított Megközelítések
1. **ContentReplacement** - HttpContent capture és újraépítés
2. **SizeLimitProtection** - DoS védelem hozzáadása
3. **PostProcessing** - Minimális capture (nincs raw body)
4. **FullPipeline** - Teljes audit logging pipeline

## Architektúra

### Composable Service Architecture
```
┌─────────────────┐    ┌──────────────────┐    ┌─────────────────┐
│   Capturing     │    │   Protection     │    │     Logging     │
│    Service      │    │    Service       │    │    Service      │
└─────────────────┘    └──────────────────┘    └─────────────────┘
         │                       │                       │
         └───────────────────────┼───────────────────────┘
                                 │
                    ┌─────────────────┐
                    │   Delegating    │
                    │    Handler      │
                    └─────────────────┘
```

### Service Compatibility Matrix

| Service Type | Compatible Patterns | Raw Body Access | Complexity |
|--------------|-------------------|-----------------|------------|
| ContentReplacement | DelegatingHandler | ✅ Full | Medium |
| StreamWrapping | HTTP Module | ✅ Full | High |
| PostProcessing | Action Filter | ❌ Processed Only | Low |

## Konfiguráció

### Dependency Injection
```csharp
private static void ConfigureDependencyInjection(HttpConfiguration config)
{
    var container = new SimpleDependencyResolver();
    
    // Register validators
    container.Register<IValidator<Person>, PersonValidator>();
    
    // Register services
    container.Register<IRequestCapturingService, ContentReplacementCapturingService>();
    container.Register<IDoSProtectionService, SizeLimitProtectionService>();
    container.Register<IAuditLoggingService, ConsoleAuditLoggingService>();
    
    config.DependencyResolver = container;
}
```

### Controller Dependency Injection
```csharp
public class TestController : ApiController
{
    private readonly IValidator<Person> _personValidator;

    public TestController(IValidator<Person> personValidator)
    {
        _personValidator = personValidator ?? throw new ArgumentNullException(nameof(personValidator));
    }

    [HttpPost]
    [Route("validation")]
    public async Task<IHttpActionResult> ValidationTest([FromBody] Person person)
    {
        if (person == null)
        {
            return BadRequest("Person data is required");
        }

        var validationResult = _personValidator.Validate(person);
        
        if (!validationResult.IsValid)
        {
            var errors = validationResult.Errors.Select(e => e.ErrorMessage).ToList();
            return BadRequest(string.Join(", ", errors));
        }

        return Ok(new { Message = "Person validation passed", Person = person });
    }
}
```

### WebApiConfig.cs
```csharp
private static void RegisterAuditServices(HttpConfiguration config)
{
    // Get services from DI container
    var capturingService = config.DependencyResolver.GetService(typeof(IRequestCapturingService)) as IRequestCapturingService;
    var protectionService = config.DependencyResolver.GetService(typeof(IDoSProtectionService)) as IDoSProtectionService;
    var auditService = config.DependencyResolver.GetService(typeof(IAuditLoggingService)) as IAuditLoggingService;

    var auditHandler = new ComposableAuditMessageHandler(
        capturingService, protectionService, auditService);

    config.MessageHandlers.Insert(0, auditHandler);
}
```

## Teljesítmény Optimalizációk

### Memória Használat
- **ContentReplacement**: Dupla memória használat a request body feldolgozásakor
- **StreamWrapping**: Inkrementális memória használat
- **SizeLimitProtection**: Temp file fallback nagy requestekhez

### Processing Overhead
- **Early Pipeline**: Minimális hatás a business logic teljesítményére
- **Stream Manipulation**: CPU overhead változó a stratégiától függően

## Biztonsági Megfontolások

### Sensitive Data Exposure
- Raw request logging érzékeny adatokat is rögzíthet
- JWT token információk óvatos kezelése szükséges
- Selective field masking vagy encryption javasolt

### DoS Protection
- Size-based protection nagy requestek ellen
- Rate limiting client behavior alapján
- Circuit breaker pattern abuse ellen

## Következő Lépések

### Phase 1: Alapvető Implementáció
- DelegatingHandler + ContentReplacement + SizeLimit

### Phase 2: Fokozott Biztonság
- CircuitBreaker pattern hozzáadása

### Phase 3: Skála Optimalizáció
- BackgroundQueue vagy StreamingDigest implementálása

### Phase 4: Enterprise Funkciók
- Early Capture + Late Log architektúra

## Fejlesztői Útmutató

### Új Service Implementálása
1. Implementálja a megfelelő interfészt
2. Adja hozzá a service compatibility matrix-hoz
3. Írjon teszteket a kritikus funkciókra
4. Benchmark a teljesítmény ellenőrzésére

### Tesztelési Stratégia
- **Unit tesztek**: XUnit framework használatával izolált komponensekre (`[UnitTest]` attribútum)
- **Acceptance tesztek**: Reqnroll + XUnit end-to-end flow validációra (`[AcceptanceTest]` attribútum)
- **Performance tesztek**: BenchmarkDotNet load alatt
- **Security tesztek**: Érzékeny adatok kezelésére

### Teszt Kategorizálás
A projekt XUnit.Categories használatával kategorizálja a teszteket:
- **UnitTest**: Izolált komponens tesztek (5 teszt)
- **AcceptanceTest**: End-to-end Reqnroll tesztek (8 teszt)

### Assertion Library
A projekt a **Shouldly** assertion library-t használja a FluentAssertions helyett, mivel:
- **Ingyenes**: A FluentAssertions 8.x verziótól kezdve fizetős
- **Expresszív**: Hasonlóan olvasható szintaxis
- **Jó hibaüzenetek**: Részletes hibaüzenetek tesztelés során

**Példa Shouldly használatára:**
```csharp
// FluentAssertions helyett
result.Should().NotBeNull();
result.Should().BeOfType<OkResult>();
content.Should().Contain("expected text");

// Shouldly használatával
result.ShouldNotBeNull();
result.ShouldBeOfType<OkResult>();
content.ShouldContain("expected text");
```

### Típus Ellenőrzés és Pattern Matching
A tesztekben **típus ellenőrzést és pattern matching-ot** használunk a reflection helyett:

```csharp
// Reflection helyett (kerülendő)
var contentProperty = result.GetType().GetProperty("Content");
var content = contentProperty?.GetValue(result);

// Típus ellenőrzés és pattern matching (javasolt)
if (result is OkNegotiatedContentResult<object> okResult)
{
    var content = okResult.Content; // Típusbiztos hozzáférés
    content.ShouldNotBeNull();
}
```

**Előnyök:**
- **Típusbiztonság**: Compile-time ellenőrzés
- **Teljesítmény**: Nincs reflection overhead
- **Olvashatóság**: Egyértelmű kód
- **IDE támogatás**: IntelliSense és refactoring

## Kapcsolódó Dokumentáció

- [docs/audit_logging_analysis-v4.md](docs/audit_logging_analysis-v4.md) - Részletes technikai elemzés
- [Reqnroll Documentation](https://reqnroll.net/) - Acceptance testing framework
- [XUnit Documentation](https://xunit.net/) - Unit testing framework
- [Shouldly Documentation](https://shouldly.io/) - Assertion library
- [BenchmarkDotNet](https://benchmarkdotnet.org/) - Performance benchmarking

## Licenc

Ez a projekt MIT licenc alatt áll rendelkezésre.

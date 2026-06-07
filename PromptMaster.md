Age como um Engenheiro de Prompts Especialista e Arquiteto de Software .NET.
O meu objetivo é que tu pegues no meu pedido (escrito no final em português) e cries o MELHOR PROMPT POSSÍVEL EM INGLÊS para eu colar numa ferramenta de IA de código (como Anti Gravity, Cursor, etc).

Regras OBRIGATÓRIAS para a tua resposta:
1. O prompt final deve ser escrito 100% em INGLÊS.
2. FORMATO DE SAÍDA CRÍTICO: Tu tens de envolver TODA a tua resposta num bloco de código markdown. É estritamente proibido escrever qualquer palavra antes ou depois dos backticks. A tua resposta deve começar exatamente com ```markdown e terminar exatamente com ```.
3. O prompt que vais gerar deve conter EXATAMENTE estas secções e com este nível de detalhe:

   - **ROLE:** "Act as an Expert .NET 8 Architect and Full Stack Developer..."
   
   - **PROJECT CONTEXT:** Include this exact text: "ASP.NET Core 8 MVC, Razor Pages, EF Core 8 SQL Server/LocalDB, ASP.NET Identity + Google Auth, Bootstrap 5 UI. Controllers interact directly with `ApplicationDbContext` via DI (No Repository Pattern). Nullable and ImplicitUsings are enabled. Project name: BarberLoc."
   
   - **THE ARCHITECTURE & TASK:** Traduz tecnicamente o meu pedido. Lista explicitamente quais Models, ViewModels, Controllers e Razor Views (arquivos .cshtml) precisam de ser criados ou alterados. Se o meu pedido envolver dados externos (ex: Google Reviews), pede à IA para criar um Interface/Service isolado para essa integração.
   
   - **STRICT CONSTRAINTS:** Inclui estas regras literais para a IA de código: 
     * NO placeholders like "/// rest of the code". Write COMPLETE, production-ready code.
     * Update `ApplicationDbContext` with new DbSets.
     * CRITICAL: Provide the exact code to update `DbSeeder.cs` to insert mock/seed data for any new models created.
     * Provide the exact EF Core CLI `dotnet ef migrations add` command.
     * Use pure Bootstrap 5 classes in Razor Views.
     
   - **EXECUTION PLAN:** Ask the code AI to output a step-by-step architectural reasoning list BEFORE writing any code block.

[O QUE EU QUERO IMPLEMENTAR:]
(Substitui este texto pela tua ideia/funcionalidade em português)

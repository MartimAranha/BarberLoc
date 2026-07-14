# BarberLoc - Plataforma de Agendamento de Barbearias

Bem-vindo ao **BarberLoc**, a sua solução moderna para encontrar e agendar serviços de barbearia.

## 🚀 Funcionalidades Principais

*   **Busca e Localização**: Encontre barbearias próximas, filtre por nome ou avaliação.
*   **Agendamento Online**: Reserve horários diretamente na plataforma.
*   **Serviços ao Domicílio**: Opção para solicitar profissionais que vão até si (tipo Uber).
*   **Avaliações e Ratings**: Veja o que outros clientes dizem antes de escolher.
*   **Perfil de Utilizador**: Gestão de dados pessoais e histórico de reservas.
*   **Design Premium**: Interface moderna, responsiva e intuitiva.

## 🛠️ Tecnologias Utilizadas

*   **ASP.NET Core 8.0 MVC**: Framework web robusto e performático.
*   **Entity Framework Core**: ORM para gestão de dados.
*   **SQL Server**: Base de dados relacional.
*   **ASP.NET Identity**: Sistema seguro de autenticação e autorização.
*   **Bootstrap 5 + Custom CSS**: Design responsivo e estilizado.

## 📦 Como Executar o Projeto

1.  **Pré-requisitos**:
    *   .NET 8.0 SDK instalado.
    *   SQL Server (LocalDB ou Express) instalado.

2.  **Configuração da Base de Dados**:
    O projeto já está configurado para usar o LocalDB. As migrações foram criadas.
    Para garantir que a base de dados está atualizada:
    ```bash
    dotnet ef database update
    ```

3.  **Executar a Aplicação(tem de ter a API KEY!)**:
    Na pasta raiz do projeto, execute:
    ```bash
    dotnet run
    ```
    A aplicação ficará disponível em `https://localhost:7288` (ou porta similar indicada no terminal).

4.  **Dados de Teste**:
    Ao iniciar pela primeira vez, a aplicação irá povoar a base de dados com barbearias, serviços e utilizadores de exemplo.
    *   **Admin**: `admin@barberloc.pt` / `Admin123!`
    *   **User**: `joao@example.com` / `User123!`

## 📱 Funcionalidades Específicas PAP

*   **SDG 8**: Apoio ao comércio local e crescimento económico.
*   **Inovação**: Integração de serviços ao domicílio.
*   **Compromisso**: Foco na experiência do utilizador masculino (+16 anos).

---
Desenvolvido por Martim Aranha - PAP 2026

using System.ComponentModel.DataAnnotations;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using WebApplication1.Models;

namespace WebApplication1.Areas.Identity.Pages.Account
{
    [AllowAnonymous]
    public class ResetPasswordModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;

        public ResetPasswordModel(UserManager<ApplicationUser> userManager)
        {
            _userManager = userManager;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new InputModel();

        /// <summary>
        /// ViewModel for the Reset Password form.
        /// Both the token and email are stored as hidden fields in the form.
        /// </summary>
        public class InputModel
        {
            // ── Hidden fields (passed via query string on GET, hidden inputs on POST) ──

            /// <summary>The URL-encoded password reset token from the email link.</summary>
            [Required]
            public string Token { get; set; } = string.Empty;

            /// <summary>The user's email address, used to look up the account.</summary>
            [Required]
            [EmailAddress]
            public string Email { get; set; } = string.Empty;

            // ── Visible form fields ────────────────────────────────────────────

            [Required(ErrorMessage = "A nova senha é obrigatória.")]
            [StringLength(100, ErrorMessage = "A senha deve ter pelo menos {2} e no máximo {1} caracteres.", MinimumLength = 6)]
            [DataType(DataType.Password)]
            [Display(Name = "Nova senha")]
            public string Password { get; set; } = string.Empty;

            [DataType(DataType.Password)]
            [Display(Name = "Confirmar nova senha")]
            [Compare("Password", ErrorMessage = "As senhas não coincidem.")]
            public string ConfirmPassword { get; set; } = string.Empty;
        }

        // GET /Identity/Account/ResetPassword?token=...&email=...
        public IActionResult OnGet(string? token, string? email)
        {
            // If the token or email are missing, someone navigated here manually
            // without a valid reset link — show them an error.
            if (token == null || email == null)
            {
                return BadRequest("Um token e email são necessários para redefinir a senha.");
            }

            // Bind token + email to the InputModel so they are available as
            // hidden fields in the rendered form, ready for the POST.
            Input = new InputModel
            {
                Token = token,
                Email = email
            };

            return Page();
        }

        // POST /Identity/Account/ResetPassword
        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }

            // ── 1. Find the user by email ─────────────────────────────────────
            var user = await _userManager.FindByEmailAsync(Input.Email);

            // Security: redirect to confirmation even if user not found.
            // An attacker cannot distinguish "token invalid" from "user not found".
            if (user == null)
            {
                return RedirectToPage("./ResetPasswordConfirmation");
            }

            // ── 2. Decode the URL-safe Base64 token back to the original token ─
            var decodedTokenBytes = WebEncoders.Base64UrlDecode(Input.Token);
            var decodedToken      = Encoding.UTF8.GetString(decodedTokenBytes);

            // ── 3. Apply the new password using Identity's secure token check ──
            // ResetPasswordAsync validates the token signature and expiry,
            // then hashes and stores the new password.
            var result = await _userManager.ResetPasswordAsync(user, decodedToken, Input.Password);

            if (result.Succeeded)
            {
                return RedirectToPage("./ResetPasswordConfirmation");
            }

            // ── 4. Surface validation errors (e.g. token expired, weak password) ──
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return Page();
        }
    }
}

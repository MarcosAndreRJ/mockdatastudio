# Comentário no estilo MySQL
SET NAMES utf8mb4;

CREATE TABLE `usuarios` (
    `id` INT AUTO_INCREMENT PRIMARY KEY,
    `nome` VARCHAR(100) NOT NULL
) ENGINE=InnoDB;

DELIMITER //

CREATE PROCEDURE sp_inserir_usuario(IN p_nome VARCHAR(100))
BEGIN
    INSERT INTO `usuarios` (`nome`) VALUES (p_nome);
    SELECT LAST_INSERT_ID();
END//

DELIMITER ;

SELECT * FROM `usuarios`;

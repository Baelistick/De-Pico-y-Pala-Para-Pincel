# ⛏️ De Pico y Pala Para Pincel 🖌️
**Un ecosistema colaborativo multijugador en tiempo real.**
*Desarrollado para la Juntos Global Game Jam Latin America (2026).*

![Godot Engine](https://img.shields.io/badge/GODOT-%23FFFFFF.svg?style=for-the-badge&logo=godot-engine) ![C#](https://img.shields.io/badge/c%23-%23239120.svg?style=for-the-badge&logo=c-sharp&logoColor=white) ![.NET](https://img.shields.io/badge/.NET-5C2D91?style=for-the-badge&logo=.net&logoColor=white) ![Supabase](https://img.shields.io/badge/Supabase-3ECF8E?style=for-the-badge&logo=supabase&logoColor=white) ![Android](https://img.shields.io/badge/Android-3DDC84?style=for-the-badge&logo=android&logoColor=white)

---

## 👁️ Visual Proof (El Ecosistema en Acción)


---

## 🧠 Arquitectura Multijugador (Mente-0)
Este proyecto prescinde de los tradicionales WebSockets a favor de una arquitectura basada en **Sincronización Delta mediante peticiones HTTP REST**. 

El cliente (Godot) se comunica directamente con una base de datos PostgreSQL alojada en **Supabase**. La lógica de red opera bajo los siguientes principios:
* **Lectura Eficiente (Delta Sync):** En lugar de descargar el mapa de 10,000 bloques (100x100) en cada fotograma, el cliente memoriza la última marca de tiempo (`updated_at`) y solo solicita al servidor los píxeles que han cambiado desde ese milisegundo exacto.
* **Autoridad Distribuida:** Cada cliente procesa la destrucción de escombros y la pintura localmente para un *feedback* visual inmediato, pero el servidor valida la propiedad de las casillas para gestionar el consumo de energía.
* **Radar Poblacional:** Un sistema de latidos (*Ping/Count*) actualiza en tiempo real a los operativos activos en la red sin saturar la base de datos.

---

## ⚠️ Post-Mortem y Desafíos
Desplegar un ecosistema multijugador con base de datos en tiempo real bajo la presión de una Game Jam generó fricciones estructurales que requirieron refactorización en caliente:

1. **El Caos del Port a Celulares:** La transición inicial a Android fue hostil, resultando en fallos críticos de instalación en los dispositivos. Fue necesario reestructurar las opciones de exportación del motor y hacer un *downgrade* táctico al renderizador **"Compatibility"** de Godot para asegurar el soporte masivo de hardware móvil.
2. **Desincronización y "Sobrescritura Fantasma":** Un conflicto de autoridad en el gestor de eventos provocó que los anillos perimetrales de tierra y piedra reaparecieran después de haber sido limpiados por los jugadores. Se solucionó centralizando la ejecución de eventos ambientales bajo un "Cortafuegos de Identidad" exclusivo para el administrador.
3. **Fuga de Rendimiento (Lag de Interfaz):** Renderizar las etiquetas de coordenadas sobre una grilla de 10,000 celdas saturó el hilo de procesamiento principal, congelando las cajas de texto en el menú de *login*. La solución fue apagar la superposición táctica por defecto y optimizar el recálculo de la barra de progreso.
4. **Inestabilidad de Conexión:** Las continuas peticiones simultáneas generaban cuellos de botella iniciales con el servidor que debieron mitigarse depurando la memoria de los nodos HTTP.

---

## 🎯 Lo que quedó en el Tintero (Recortes de Producción)
Por la naturaleza de la Game Jam, ciertas características fueron aparcadas para garantizar la estabilidad del núcleo jugable:
* **Exportación Web (HTML5):** La arquitectura de Godot 4.x con C# .NET presenta bloqueos estrictos de compilación AOT para navegadores, por lo que el despliegue se limitó a PC y Android.
* **Integración Orgánica del Cuentagotas:** Aunque funcional, la herramienta de clonación de pintura requiere una mejor adaptación en interfaces táctiles pequeñas para no interrumpir el flujo del usuario.
* **Optimización Extrema de Servidor:** Escalabilidad pendiente en las cuotas de peticiones a la API para soportar multitudes masivas sin latencia.
* **Infraestructura de Seguridad:** Transicionar del actual sistema de contraseñas *hasheadas* localmente a tokens JWT validados directamente por Supabase Auth.

---

## ⚙️ How to Run (Setup del Laboratorio)

1. **Clonar el Repositorio:**
   ```bash
   git clone [https://github.com/TuUsuario/PicoPalaPincel.git](https://github.com/TuUsuario/PicoPalaPincel.git)

## Entorno del Motor:

Requiere Godot Engine 4.x (Versión .NET).

Instalación previa del SDK de .NET 8 o superior.

## Variables de Entorno (Supabase):

Renombra el archivo secrets.example.cfg a secrets.cfg en la raíz del proyecto.

Ingresa tu URL y tu Anon Key del panel de Supabase en dicho archivo (el repositorio ignora este archivo por seguridad).

Ejecuta los scripts SQL provistos para generar las tablas usuarios, pixels y las funciones RPC.

Compilación: Ejecutar el proyecto desde el editor de Godot (F5) o exportar usando los presets de Windows Desktop o Android (Renderizador: Compatibility).
